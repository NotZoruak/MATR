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
using MFAAvalonia.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MFAAvalonia.Views.Pages;

/// <summary>按日期分组显示每振刀剑的内番结算结果。</summary>
public partial class DutyRecordsView : UserControl
{
    private sealed record Row(GameOverviewLogParser.DutyRecord Record, string Name, string Growth, string Outcome, bool IsMaximum);
    public sealed record DutySummary(int RecordCount, int DateCount, int NoGrowthCount, IReadOnlyDictionary<string, long> Gains);
    private static readonly JObject SwordCatalog = HeldSwordTableData.LoadCatalog();
    private readonly string _directory;
    private readonly List<GameOverviewLogParser.DutyRecord> _records = [];
    private string _account = "";
    private string _search = "";
    private DateTime? _startDate;
    private DateTime? _endDate;

    public DutyRecordsView(string directory)
    {
        _directory = directory;
        InitializeComponent();
    }

    public void AddRecords(string account, IEnumerable<GameOverviewLogParser.DutyRecord> records)
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
                if (File.Exists(path)) _records.AddRange(JsonConvert.DeserializeObject<List<GameOverviewLogParser.DutyRecord>>(File.ReadAllText(path)) ?? []);
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
        _startDate = startDate?.Date;
        _endDate = endDate?.Date;
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
        if (rows.Length == 0)
        {
            var emptyGrid = CreateGrid();
            emptyGrid.ItemsSource = Array.Empty<Row>();
            GroupsPanel.Children.Add(emptyGrid);
        }
        foreach (var group in rows.GroupBy(x => DateOnly.TryParse(x.Record.Time[..Math.Min(10, x.Record.Time.Length)], CultureInfo.InvariantCulture, out var day) ? day : DateOnly.MinValue)
                     .OrderByDescending(x => x.Key))
        {
            var card = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Stretch };
            card.Children.Add(new TextBlock { Text = group.Key == DateOnly.MinValue ? "日期未知" : group.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(4, 4) });
            var grid = CreateGrid();
            grid.ItemsSource = group.OrderByDescending(x => x.Record.Time, StringComparer.Ordinal).ToArray();
            card.Children.Add(grid);
            GroupsPanel.Children.Add(card);
        }
        EmptyText.IsVisible = rows.Length == 0;
    }

    private static DataGrid CreateGrid()
    {
        var grid = new DataGrid { Name = "RecordsGrid", AutoGenerateColumns = false, IsReadOnly = true, CanUserSortColumns = false, CanUserReorderColumns = false, CanUserResizeColumns = true, GridLinesVisibility = DataGridGridLinesVisibility.All, HeadersVisibility = DataGridHeadersVisibility.Column, RowHeight = 40, ColumnHeaderHeight = 36, MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        grid.Columns.Add(new DataGridTextColumn { Header = "时间", Binding = new Avalonia.Data.Binding(nameof(Row.Record) + ".Time"), Width = new DataGridLength(164) });
        grid.Columns.Add(new DataGridTemplateColumn { Header = "刀剑名", Width = new DataGridLength(1, DataGridLengthUnitType.Star), CellTemplate = new FuncDataTemplate<Row>((row, _) => {
            var name = new TextBlock { Text = row?.Name ?? "未知刀剑", VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
            ToolTip.SetTip(name, row == null ? null : "唯一 ID：" + row.Record.SwordSerial);
            return name;
        }) });
        grid.Columns.Add(new DataGridTextColumn { Header = "项目", Binding = new Avalonia.Data.Binding(nameof(Row.Record) + ".DutyType"), Width = new DataGridLength(84) });
        grid.Columns.Add(new DataGridTemplateColumn { Header = "结算结果", Width = new DataGridLength(1, DataGridLengthUnitType.Star), CellTemplate = new FuncDataTemplate<Row>((row, _) => {
            var border = new Border { Background = Brushes.Transparent, Padding = new Thickness(1, 2), HorizontalAlignment = HorizontalAlignment.Stretch };
            border.Child = new TextBlock { Text = row?.Outcome ?? "—", VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
            if (row?.IsMaximum == true) border.Bind(Border.BackgroundProperty, new DynamicResourceExtension("SwordMaximumBrush"));
            return border;
        }) });
        grid.Columns.Add(new DataGridTextColumn { Header = "", Width = new DataGridLength(16), IsReadOnly = true, CanUserResize = false, CanUserReorder = false, CanUserSort = false });
        return grid;
    }

    private bool MatchesFilters(Row row)
    {
        if (!DateTime.TryParseExact(row.Record.Time, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp)) return false;
        if (_startDate.HasValue && timestamp.Date < _startDate.Value) return false;
        if (_endDate.HasValue && timestamp.Date > _endDate.Value) return false;
        if (string.IsNullOrWhiteSpace(_search)) return true;
        var searchable = string.Join(" ", row.Record.Time, row.Record.DutyType, row.Name, row.Growth, row.Outcome);
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

    private static Row BuildRow(GameOverviewLogParser.DutyRecord record)
    {
        var sword = SwordCatalog["swords"]?[record.SwordId] as JObject;
        var name = sword?["name"]?.ToString() ?? "刀剑 ID：" + record.SwordId;
        if (sword?["symbol"]?.ToString() == "2") name += "·极";
        var (key, label) = record.UpType switch { 2 => ("def", "防御"), 3 => ("hp", "生存"), _ => ("", $"未知属性（类型 {record.UpType}）") };
        var growth = record.Success && record.UpValue > 0 ? $"{label}+{record.UpValue}" : $"{label}未增长";
        var data = record.SwordData;
        var current = long.TryParse(data[key == "hp" ? "hp_max" : key]?.ToString(), out var hp) && key == "hp"
            ? hp : long.TryParse(data[key]?.ToString(), out var stat) ? stat : (long?)null;
        long? total = long.TryParse(sword?[key + "_up"]?.ToString(), out var cap) ? cap : null;
        long? remaining = long.TryParse(data[key + "_up"]?.ToString(), out var left) ? left : null;
        var gained = total.HasValue && remaining.HasValue && total >= remaining ? total - remaining : null;
        var result = current?.ToString(CultureInfo.InvariantCulture) ?? "—";
        if (gained.HasValue && total.HasValue) result += $"（{gained}/{total}）";
        var maximum = record.IsMaximum || remaining == 0;
        var outcome = growth + "，" + result;
        return new Row(record, name, growth, outcome, maximum);
    }

    private static string AttributeName(int type) => type switch { 2 => "防御", 3 => "生存", _ => $"未知属性（类型 {type}）" };
}
