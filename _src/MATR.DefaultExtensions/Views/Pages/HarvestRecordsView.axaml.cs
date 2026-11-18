using System.Security.Cryptography;
using System.Text;
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
            if (parts.Length >= 3) return string.Join(':', parts.Take(3)) + ":resource:" + record.Name;
        }
        return record.Id;
    }

    private void ShowSwords(object? sender, RoutedEventArgs e) { _swords = true; ShowTable(); }
    private void ShowResources(object? sender, RoutedEventArgs e) { _swords = false; ShowTable(); }
    private void SearchChanged(object? sender, TextChangedEventArgs e) => ShowTable();
    private async void ClearClicked(object? sender, RoutedEventArgs e)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner == null) return;
        var result = await SukiMessageBox.ShowDialog(owner, new SukiMessageBoxHost
        {
            Content = "确定清除所有账号保存的刀剑与资源记录？此操作无法撤销。",
            ActionButtonsPreset = SukiMessageBoxButtons.YesNo,
            IconPreset = SukiMessageBoxIcons.Warning,
        }, new SukiMessageBoxOptions { Title = "清除收获记录" });
        if (result is not SukiMessageBoxResult.Yes) return;
        _records.Clear();
        foreach (var path in Directory.GetFiles(_directory, "harvest-*.json")) File.Delete(path);
        ShowTable();
    }

    private void ShowTable(bool preservePosition = false)
    {
        SwordTab.Classes.Set("selected", _swords);
        ResourceTab.Classes.Set("selected", !_swords);
        if (_columnsConfiguredForSwords != _swords)
        {
            RecordsGrid.Columns.Clear();
            AddColumn("时间", nameof(Row.Time), 176);
            AddColumn(_swords ? "刀剑名" : "资源种类", nameof(Row.Name), _swords ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(115));
            if (_swords) AddColumn("刀种", nameof(Row.Type), 92);
            else AddColumn("数量", nameof(Row.Amount), 92);
            AddColumn("来源", nameof(Row.SourceText), new DataGridLength(1, DataGridLengthUnitType.Star));
            _columnsConfiguredForSwords = _swords;
        }
        var rows = _records.Where(record => record.Kind == (_swords ? "sword" : "resource"))
            .Select(record => new Row(record.Id, record.Time, DisplayName(record), SwordType(record), record.Amount,
                record.Source + (record.Map == null ? "" : " · " + record.Map) + (record.Point == null ? "" : " 点位 " + record.Point)))
            .Where(row => MatchesSearch(row, SearchBox?.Text))
            .OrderByDescending(row => row.Time).ToArray();
        UpdateVisibleRows(rows, preservePosition);
        EmptyText.Text = rows.Length == 0
            ? _swords ? "暂无刀剑获取记录；当前游戏日志中没有检测到刀剑掉落。" : "暂无资源获取记录"
            : "";
        EmptyText.IsVisible = rows.Length == 0;
    }

    private static bool MatchesSearch(Row row, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        var searchable = string.Join(" ", row.Time, row.Name, row.Type, row.Amount, row.SourceText);
        if (search == "成功") searchable = searchable.Replace("大成功", "", StringComparison.Ordinal);
        return searchable.Contains(search, StringComparison.OrdinalIgnoreCase);
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
