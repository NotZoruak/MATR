using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using System.Collections.ObjectModel;
using Newtonsoft.Json.Linq;
using MFAAvalonia.Services;
using Newtonsoft.Json;
using SukiUI.Controls;
using SukiUI.MessageBox;

namespace MFAAvalonia.Views.Pages;

/// <summary>显示并保存日志明确记录的刀剑与资源收获。</summary>
public partial class HarvestRecordsView : UserControl
{
    private readonly string _directory;
    private readonly List<GameOverviewLogParser.HarvestEntry> _records = [];
    private readonly ObservableCollection<Row> _visibleRows = [];
    private string _account = "";
    private string _appliedSearch = "";
    private bool _swords;
    private bool? _columnsConfiguredForSwords;
    private static readonly JObject SwordCatalog = HeldSwordTableData.LoadCatalog();
    private static readonly string[] SwordTypes = ["未知", "短刀", "胁差", "打刀", "太刀", "大太刀", "枪", "薙刀", "剑"];

    public HarvestRecordsView(string directory)
    {
        _directory = directory;
        InitializeComponent();
        RecordsGrid.ItemsSource = _visibleRows;
        ShowTable();
    }

    public void AddRecords(string account, IEnumerable<GameOverviewLogParser.HarvestEntry> records)
    {
        if (string.IsNullOrWhiteSpace(account)) return;
        var accountChanged = _account != account;
        var changed = accountChanged;
        if (accountChanged)
        {
            _account = account;
            _records.Clear();
            try
            {
                var path = StorePath();
                if (File.Exists(path)) _records.AddRange(JsonConvert.DeserializeObject<List<GameOverviewLogParser.HarvestEntry>>(File.ReadAllText(path)) ?? []);
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException) { }
        }
        var known = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < _records.Count; index++)
            known.TryAdd(EventIdentity(_records[index]), index);
        foreach (var record in records)
        {
            var identity = EventIdentity(record);
            if (known.TryGetValue(identity, out var index))
            {
                if (_records[index] != record)
                {
                    _records[index] = record;
                    changed = true;
                }
                continue;
            }
            known[identity] = _records.Count;
            _records.Add(record);
            changed = true;
        }
        if (!changed) return;
        Directory.CreateDirectory(_directory);
        File.WriteAllText(StorePath(), JsonConvert.SerializeObject(_records));
        ShowTable(preservePosition: !accountChanged);
    }

    private string StorePath() => Path.Combine(_directory, "harvest-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_account))).ToLowerInvariant() + ".json");

    private static string EventIdentity(GameOverviewLogParser.HarvestEntry record)
    {
        if (record.Kind == "resource")
        {
            var parts = record.Id.Split(':');
            if (parts.Length >= 7)
                return string.Join(':', parts.Take(UsesStableResourceIdentity(parts[4]) ? 3 : 4)) + ":resource:" + parts[4];
            if (parts.Length >= 4)
                return string.Join(':', parts.Take(4)) + ":resource:" + (LegacyRewardItemId(record.Name) ?? record.Name);
        }
        return record.Id;
    }

    private static bool UsesStableResourceIdentity(string itemId) => itemId is "4206" or "6101" or "6103" or "6105" or "6107" or "6109" or "6111" or "6113";

    private static string? LegacyRewardItemId(string name)
    {
        var knownId = name switch
        {
            "委托符" => "1",
            "木炭" => "2",
            "玉钢" => "3",
            "冷却材" => "4",
            "砥石" => "5",
            _ => null
        };
        if (knownId != null) return knownId;
        var markerIndex = name.LastIndexOf("ID ", StringComparison.Ordinal);
        if (markerIndex < 0) return null;
        var endIndex = name.IndexOf('）', markerIndex);
        return name[(markerIndex + 3)..(endIndex < 0 ? name.Length : endIndex)];
    }

    private void ShowSwords(object? sender, RoutedEventArgs e) { _swords = true; ShowTable(); }
    private void ShowResources(object? sender, RoutedEventArgs e) { _swords = false; ShowTable(); }
    private void SearchClicked(object? sender, RoutedEventArgs e)
    {
        _appliedSearch = SearchBox.Text ?? "";
        ShowTable();
    }
    private void DateFilterChanged(object? sender, SelectionChangedEventArgs e) => ShowTable();
    private void ClearDateFilter(object? sender, RoutedEventArgs e)
    {
        StartDatePicker.SelectedDate = null;
        EndDatePicker.SelectedDate = null;
        ShowTable();
    }

    private async void SummaryClicked(object? sender, RoutedEventArgs e)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner == null) return;

        var rows = FilteredRows();
        var groups = rows.GroupBy(row => row.Name, StringComparer.Ordinal)
            .Select(group => new { Name = group.Key, Count = _swords ? group.Count() : group.Sum(row => row.Amount), Type = group.First().Type })
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.Name, StringComparer.Ordinal)
            .ToArray();
        var total = _swords ? rows.Length : rows.Sum(row => row.Amount);
        var subtitle = $"时间：{StartDatePicker.SelectedDate?.ToString("yyyy-MM-dd") ?? "不限"} 至 {EndDatePicker.SelectedDate?.ToString("yyyy-MM-dd") ?? "不限"}";
        if (!string.IsNullOrWhiteSpace(_appliedSearch)) subtitle += $"\n搜索：{_appliedSearch}";

        var detailRows = new StackPanel { Spacing = 12 };
        var sections = _swords
            ? groups.GroupBy(group => string.IsNullOrWhiteSpace(group.Type) ? "未知" : group.Type)
                .OrderBy(group => Array.IndexOf(SwordTypes, group.Key) is var i && i >= 0 ? i : int.MaxValue)
                .ToArray()
            : groups.GroupBy(_ => "").ToArray();
        foreach (var section in sections)
        {
            var sectionRow = new Grid { ColumnDefinitions = new ColumnDefinitions(_swords ? "64,*" : "*") };
            if (_swords)
                sectionRow.Children.Add(new TextBlock { Text = section.Key + "：", FontWeight = Avalonia.Media.FontWeight.Bold });
            var items = new WrapPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, ItemSpacing = 14, LineSpacing = 6 };
            foreach (var (group, index) in section.Select((group, index) => (group, index)))
                items.Children.Add(new TextBlock { Text = _swords ? $"{group.Name}（x{group.Count}）" + (index < section.Count() - 1 ? "，" : "") : $"{group.Name} × {group.Count:N0}" });
            Grid.SetColumn(items, _swords ? 1 : 0);
            sectionRow.Children.Add(items);
            detailRows.Children.Add(sectionRow);
        }
        if (groups.Length == 0)
            detailRows.Children.Add(new TextBlock { Text = "当前筛选条件下没有记录。", Margin = new Avalonia.Thickness(0, 8) });

        var content = new StackPanel { Width = 620, Spacing = 10 };
        content.Children.Add(new TextBlock { Text = subtitle, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        content.Children.Add(new TextBlock
        {
            Text = _swords ? $"共 {total} 振，{groups.Length} 种刀剑" : $"共 {rows.Length} 条资源记录，{groups.Length} 种资源",
            FontWeight = Avalonia.Media.FontWeight.SemiBold
        });
        content.Children.Add(new Border
        {
            Height = 1,
            Background = Avalonia.Media.Brushes.LightGray
        });
        content.Children.Add(new ScrollViewer
        {
            Content = detailRows,
            MaxHeight = 360,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        });
        await SukiMessageBox.ShowDialog(owner, new SukiMessageBoxHost
        {
            Content = content,
            ActionButtonsPreset = SukiMessageBoxButtons.OK,
            IconPreset = null
        }, new SukiMessageBoxOptions { Title = "收获数据汇总", CanMinimize = false });
    }

    private async void ClearClicked(object? sender, RoutedEventArgs e)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner == null) return;
        var result = await SukiMessageBox.ShowDialog(owner, new SukiMessageBoxHost
        {
            Content = "确定清除当前账号保存的刀剑与资源记录？此操作无法撤销。",
            ActionButtonsPreset = SukiMessageBoxButtons.YesNo,
            IconPreset = SukiMessageBoxIcons.Warning,
        }, new SukiMessageBoxOptions { Title = "清除收获记录" });
        if (result is not SukiMessageBoxResult.Yes) return;
        _records.Clear();
        var path = StorePath();
        if (File.Exists(path)) File.Delete(path);
        ShowTable();
    }

    private void ShowTable(bool preservePosition = false)
    {
        SwordTab.Classes.Set("selected", _swords);
        ResourceTab.Classes.Set("selected", !_swords);
        if (_columnsConfiguredForSwords != _swords)
        {
            RecordsGrid.Columns.Clear();
            AddColumn("时间", nameof(Row.Time), 164);
            AddColumn(_swords ? "刀剑名" : "资源种类", nameof(Row.Name), _swords ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(105));
            if (_swords) AddColumn("刀种", nameof(Row.Type), 84);
            else AddColumn("数量", nameof(Row.Amount), 80);
            AddColumn("来源", nameof(Row.SourceText), new DataGridLength(1, DataGridLengthUnitType.Star));
            RecordsGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "",
                Width = new DataGridLength(16),
                IsReadOnly = true,
                CanUserResize = false,
                CanUserReorder = false,
                CanUserSort = false
            });
            _columnsConfiguredForSwords = _swords;
        }
        var rows = FilteredRows();
        UpdateVisibleRows(rows, preservePosition);
        EmptyText.Text = rows.Length == 0
            ? _swords ? "当前筛选条件下暂无刀剑获取记录。" : "当前筛选条件下暂无资源获取记录。"
            : "";
        EmptyText.IsVisible = rows.Length == 0;
    }

    private Row[] FilteredRows() => _records.Where(record => record.Kind == (_swords ? "sword" : "resource"))
        .Select(record => new Row(record.Id, record.Time, DisplayName(record), SwordType(record), record.Amount,
            FormatSource(record)))
        .Where(row => MatchesDateRange(row.Time) && MatchesSearch(row, _appliedSearch))
        .OrderByDescending(row => row.Time).ToArray();

    private static string FormatSource(GameOverviewLogParser.HarvestEntry record)
    {
        var map = record.Map;
        if (record.Source == "异去掉落" && map?.StartsWith("异去 ", StringComparison.Ordinal) == true)
            map = map[3..];
        return record.Source + (map == null ? "" : " · " + map) + (record.Point == null ? "" : " 点位 " + record.Point);
    }

    private bool MatchesDateRange(string value)
    {
        if (!DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var timestamp)) return false;
        var start = StartDatePicker.SelectedDate?.Date;
        var end = EndDatePicker.SelectedDate?.Date;
        return (start == null || timestamp.Date >= start.Value)
            && (end == null || timestamp.Date <= end.Value);
    }

    private static bool MatchesSearch(Row row, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        var searchable = string.Join(" ", row.Time, row.Name, row.Type, row.Amount, row.SourceText);
        foreach (var token in search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var excluded = token[0] == '-';
            var term = token[0] is '+' or '-' ? token[1..] : token;
            if (term.Length == 0) continue;
            var searchText = term == "成功" ? searchable.Replace("大成功", "", StringComparison.Ordinal) : searchable;
            var contains = searchText.Contains(term, StringComparison.OrdinalIgnoreCase);
            if (excluded ? contains : !contains) return false;
        }
        return true;
    }

    private void UpdateVisibleRows(IReadOnlyList<Row> rows, bool preservePosition)
    {
        if (!preservePosition)
        {
            _visibleRows.Clear();
            foreach (var row in rows) _visibleRows.Add(row);
            return;
        }

        var targetIndex = 0;
        foreach (var row in rows)
        {
            if (targetIndex < _visibleRows.Count && _visibleRows[targetIndex].Id == row.Id)
            {
                if (_visibleRows[targetIndex] != row) _visibleRows[targetIndex] = row;
                targetIndex++;
                continue;
            }

            var existingIndex = -1;
            for (var index = targetIndex + 1; index < _visibleRows.Count; index++)
                if (_visibleRows[index].Id == row.Id)
                {
                    existingIndex = index;
                    break;
                }

            if (existingIndex >= 0)
            {
                _visibleRows.Move(existingIndex, targetIndex);
                if (_visibleRows[targetIndex] != row) _visibleRows[targetIndex] = row;
            }
            else
            {
                _visibleRows.Insert(targetIndex, row);
            }
            targetIndex++;
        }

        while (_visibleRows.Count > rows.Count) _visibleRows.RemoveAt(_visibleRows.Count - 1);
    }

    private void AddColumn(string header, string path, double width) =>
        AddColumn(header, path, new DataGridLength(width));

    private void AddColumn(string header, string path, DataGridLength width) => RecordsGrid.Columns.Add(new DataGridTextColumn
    {
        Header = header,
        Binding = new Binding(path),
        Width = width
    });

    private static string DisplayName(GameOverviewLogParser.HarvestEntry entry)
    {
        if (entry.Kind != "sword" || entry.SwordId == null) return entry.Name;
        var definition = SwordCatalog["swords"]?[entry.SwordId] as JObject;
        var name = definition?["name"]?.ToString() ?? entry.Name;
        return definition?["symbol"]?.ToString() == "2" ? name + "·极" : name;
    }

    private static string SwordType(GameOverviewLogParser.HarvestEntry entry)
    {
        if (entry.SwordId == null || !int.TryParse(SwordCatalog["swords"]?[entry.SwordId]?["type"]?.ToString(), out var type)
            || type < 0 || type >= SwordTypes.Length) return "未知";
        return SwordTypes[type];
    }

    public sealed record Row(string Id, string Time, string Name, string Type, int Amount, string SourceText);
}
