using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Markup.Xaml.MarkupExtensions;
using MFAAvalonia.Helper;
using MFAAvalonia.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MFAAvalonia.Views.Pages;

/// <summary>按日期分组显示每振刀剑的内番结算结果。</summary>
public partial class DutyRecordsView : UserControl
{
    private sealed record Settlement(string Text, bool IsMaximum, bool Increased);
    private sealed record Row(GameOverviewDutyRecord Record, string Name, Settlement First, Settlement Second);
    public sealed record DutySummary(int RecordCount, int DateCount, int NoGrowthCount, IReadOnlyDictionary<string, long> Gains);
    private static readonly JObject SwordCatalog = HeldSwordTableData.LoadCatalog();
    private readonly string _directory;
    private readonly List<GameOverviewDutyRecord> _records = [];
    private string _account = "";
    private string _search = "";
    private DateTime? _startDate;
    private DateTime? _endDate;

    public DutyRecordsView() : this(Path.Combine(AppPaths.ConfigDirectory, "honmaru", "default", "harvest")) { }

    public DutyRecordsView(string directory)
    {
        _directory = directory;
        InitializeComponent();
    }

    public void AddRecords(string account, IEnumerable<GameOverviewDutyRecord> records)
    {
        if (string.IsNullOrWhiteSpace(account)) return;
        var accountChanged = account != _account;
        if (accountChanged)
        {
            _account = account;
            _records.Clear();
            try
            {
                var path = StorePath();
                if (File.Exists(path)) _records.AddRange(JsonConvert.DeserializeObject<List<GameOverviewDutyRecord>>(File.ReadAllText(path)) ?? []);
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException) { }
        }

        var known = _records.Select((record, index) => (record.Id, index)).ToDictionary(x => x.Id, x => x.index, StringComparer.Ordinal);
        var changed = accountChanged;
        foreach (var record in records)
        {
            if (known.TryGetValue(record.Id, out var index))
            {
                if (_records[index] != record) { _records[index] = record; changed = true; }
                continue;
            }
            known[record.Id] = _records.Count;
            _records.Add(record);
            changed = true;
        }
        if (changed)
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(StorePath(), JsonConvert.SerializeObject(_records));
        }
        Render();
    }

    public void ClearRecords()
    {
        _records.Clear();
        var path = StorePath();
        if (File.Exists(path)) File.Delete(path);
        Render();
    }

    public void SetFilters(string search, DateTime? startDate, DateTime? endDate)
    {
        _search = search;
        _startDate = startDate;
        _endDate = endDate;
        Render();
    }

    public DutySummary CreateSummary()
    {
        var rows = _records.Select(BuildRow).Where(MatchesFilters).ToArray();
        var gains = rows.Where(row => row.Record.Success && row.Record.UpValue > 0)
            .GroupBy(row => row.Record.DutyType + " · " + AttributeName(row.Record.UpType), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(row => (long)row.Record.UpValue), StringComparer.Ordinal);
        var dateCount = rows.Select(row => row.Record.Time.Length >= 10 ? row.Record.Time[..10] : "日期未知").Distinct(StringComparer.Ordinal).Count();
        return new DutySummary(rows.Length, dateCount, rows.Count(row => !row.Record.Success || row.Record.UpValue <= 0), gains);
    }

    private string StorePath() => Path.Combine(_directory, "duty-records-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(_account))).ToLowerInvariant() + ".json");

    private void Render()
    {
        GroupsPanel.Children.Clear();
        var rows = _records.Select(BuildRow)
            .Where(MatchesFilters)
            .OrderByDescending(x => x.Record.Time, StringComparer.Ordinal)
            .ThenBy(x => x.Name, StringComparer.CurrentCulture).ToArray();
        var grid = CreateGrid();
        grid.ItemsSource = rows;
        GroupsPanel.Children.Add(grid);
        EmptyText.IsVisible = rows.Length == 0;
    }

    private static DataGrid CreateGrid()
    {
        var grid = new DataGrid { Name = "RecordsGrid", AutoGenerateColumns = false, IsReadOnly = true, CanUserSortColumns = false, CanUserReorderColumns = false, CanUserResizeColumns = true, GridLinesVisibility = DataGridGridLinesVisibility.All, HeadersVisibility = DataGridHeadersVisibility.Column, RowHeight = 40, ColumnHeaderHeight = 36, MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        grid.Columns.Add(new DataGridTextColumn { Header = "时间", Binding = new Avalonia.Data.Binding(nameof(Row.Record) + ".Time"), Width = new DataGridLength(164) });
        grid.Columns.Add(new DataGridTemplateColumn { Header = "刀剑名", Width = new DataGridLength(1, DataGridLengthUnitType.Star), CellTemplate = new FuncDataTemplate<Row>((row, _) => {
            var name = new TextBlock { Text = row?.Name ?? "未知刀剑", VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
            ToolTip.SetTip(name, row == null ? null : "唯一 ID：" + row.Record.SwordSerial);
            return name;
        }) });
        grid.Columns.Add(new DataGridTextColumn { Header = "项目", Binding = new Avalonia.Data.Binding(nameof(Row.Record) + ".DutyType"), Width = new DataGridLength(84) });
        for (var index = 0; index < 2; index++)
        {
            var resultIndex = index;
            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = "结算结果 " + (index + 1), Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                CellTemplate = new FuncDataTemplate<Row>((row, _) =>
                {
                    var result = resultIndex == 0 ? row?.First : row?.Second;
                    var border = new Border { Background = Brushes.Transparent, Padding = new Thickness(1, 2), HorizontalAlignment = HorizontalAlignment.Stretch };
                    var text = new TextBlock { Text = result?.Text ?? "—", VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
                    if (result?.Increased == true) text.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("OverviewSuccessBrush"));
                    border.Child = text;
                    if (result?.IsMaximum == true) border.Bind(Border.BackgroundProperty, new DynamicResourceExtension("SwordMaximumBrush"));
                    return border;
                })
            });
        }
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "",
            Width = new DataGridLength(16),
            IsReadOnly = true,
            CanUserResize = false,
            CanUserReorder = false,
            CanUserSort = false
        });
        return grid;
    }

    private bool MatchesFilters(Row row)
    {
        if (!DateTime.TryParseExact(row.Record.Time, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp)) return false;
        if (_startDate.HasValue && timestamp < _startDate.Value) return false;
        if (_endDate.HasValue && timestamp > _endDate.Value) return false;
        if (string.IsNullOrWhiteSpace(_search)) return true;
        var searchable = string.Join(" ", row.Record.Time, row.Record.DutyType, row.Name, row.First.Text, row.Second.Text);
        foreach (var token in _search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var excluded = token[0] == '-';
            var term = token[0] is '+' or '-' ? token[1..] : token;
            if (term.Length == 0) continue;
            var contains = searchable.Contains(term, StringComparison.OrdinalIgnoreCase);
            if (excluded ? contains : !contains) return false;
        }
        return true;
    }

    private static Row BuildRow(GameOverviewDutyRecord record)
    {
        var sword = SwordCatalog["swords"]?[record.SwordId] as JObject;
        var name = sword?["name"]?.ToString() ?? "刀剑 ID：" + record.SwordId;
        if (sword?["symbol"]?.ToString() == "2") name += "·极";
        var attributes = record.DutyType switch
        {
            "饲马" => new[] { ("def", "防御"), ("mobile", "机动") },
            "耕作" => new[] { ("hp", "生存"), ("scout", "侦察") },
            "手合" or "切磋" => new[] { ("atk", "打击"), ("back", "冲力") },
            _ => new[] { GrowthAttribute(record.UpType), ("", "未知属性") }
        };
        return new Row(record, name,
            BuildSettlement(record, sword, attributes[0].Item1, attributes[0].Item2),
            BuildSettlement(record, sword, attributes[1].Item1, attributes[1].Item2));
    }

    private static Settlement BuildSettlement(GameOverviewDutyRecord record, JObject? sword, string key, string label)
    {
        var isGrowthAttribute = GrowthAttribute(record.UpType).Key == key;
        var increased = isGrowthAttribute && record.Success && record.UpValue > 0;
        long? total = long.TryParse(sword?[key + "_up"]?.ToString(), out var cap) ? cap : null;
        long? remaining = long.TryParse(record.SwordData[key + "_up"]?.ToString(), out var left) ? left : null;
        var ratio = total.HasValue && remaining.HasValue && remaining >= 0 && total >= remaining
            ? $"{total - remaining}/{total}" : "—";
        var maximum = remaining == 0 || isGrowthAttribute && record.IsMaximum;
        return new Settlement(label + (increased ? "+" + record.UpValue.ToString(CultureInfo.InvariantCulture) : "-") + "（" + ratio + "）", maximum, increased);
    }

    // 客户端结算类型顺序：机动、防御、侦察、生存、打击、冲力。
    private static (string Key, string Label) GrowthAttribute(int type) => type switch
    {
        1 => ("mobile", "机动"), 2 => ("def", "防御"), 3 => ("scout", "侦察"),
        4 => ("hp", "生存"), 5 => ("atk", "打击"), 6 => ("back", "冲力"),
        _ => ("", $"未知属性（类型 {type}）")
    };

    private static string AttributeName(int type) => GrowthAttribute(type).Label;
}
