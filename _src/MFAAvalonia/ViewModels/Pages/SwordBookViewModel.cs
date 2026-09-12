using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaaFramework.Binding;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.ValueType;
using MFAAvalonia.Models;
using MFAAvalonia.Services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SukiUI.Controls;
using SukiUI.Dialogs;
using SukiUI.MessageBox;

namespace MFAAvalonia.ViewModels.Pages;

/// <summary>刀帐页面，管理刀剑条目和四类立绘拥有状态。</summary>
public partial class SwordBookViewModel : ViewModelBase
{
    private readonly Dictionary<string, SwordBookEntry> _savedEntries = new(StringComparer.Ordinal);
    private static readonly string DraftPath = Path.Combine(AppPaths.ConfigDirectory, "swordbook_scan.json");
    /// <summary>刀种筛选的固定排列顺序；目录里将来新增的刀种排在最后。</summary>
    private static readonly string[] TypeOrder = ["短刀", "胁差", "打刀", "太刀", "大太刀", "枪", "薙刀", "剑"];

    public ObservableCollection<SwordBookRowViewModel> Entries { get; } = [];
    public ObservableCollection<SwordBookRowViewModel> VisibleEntries { get; } = [];
    public ObservableCollection<SwordBookTypeFilterOption> TypeFilterOptions { get; } = [];
    public IReadOnlyList<SwordBookFilterOption> OwnedFilterOptions { get; } = CreateFilterOptions("已拥有");
    public IReadOnlyList<SwordBookFilterOption> WoundedFilterOptions { get; } = CreateFilterOptions("中伤");
    public IReadOnlyList<SwordBookFilterOption> TrueSwordFilterOptions { get; } = CreateFilterOptions("真剑");
    public IReadOnlyList<SwordBookFilterOption> InnerCareFilterOptions { get; } = CreateFilterOptions("内番");
    public IReadOnlyList<SwordBookFilterOption> CasualFilterOptions { get; } = CreateFilterOptions("轻装");
    public string Instruction => "请先将游戏页面切换至序号最小的已拥有刀剑男士的刀帐页面，自动识别将从当前刀剑开始扫描。";
    public bool HasUnsavedChanges => Entries.Any(HasChanged);
    public bool IsIdle => !IsRecognizing;

    [ObservableProperty] private bool _isRecognizing;
    [ObservableProperty] private bool _isTypeFilterOpen;
    [ObservableProperty] private bool _hasTypeFilter;
    [ObservableProperty] private SwordBookFilterOption _ownedFilter;
    [ObservableProperty] private SwordBookFilterOption _woundedFilter;
    [ObservableProperty] private SwordBookFilterOption _trueSwordFilter;
    [ObservableProperty] private SwordBookFilterOption _innerCareFilter;
    [ObservableProperty] private SwordBookFilterOption _casualFilter;

    public SwordBookViewModel()
    {
        _ownedFilter = OwnedFilterOptions[0];
        _woundedFilter = WoundedFilterOptions[0];
        _trueSwordFilter = TrueSwordFilterOptions[0];
        _innerCareFilter = InnerCareFilterOptions[0];
        _casualFilter = CasualFilterOptions[0];

        LoadCatalog();
        LoadSavedState();
        UpdateDataPersistenceService.SwordBookDataSaved += OnSwordBookDataSaved;
    }

    [RelayCommand(CanExecute = nameof(HasUnsavedChanges))]
    private void Save()
    {
        var values = Entries.ToDictionary(row => row.Number, ToEntry, StringComparer.Ordinal);
        _savedEntries.Clear();
        foreach (var pair in values)
            _savedEntries[pair.Key] = pair.Value.Clone();
        ConfigurationManager.Current.SetValue(ConfigurationKeys.SwordBookEntries, values.Values.Select(ToState).ToList());
        foreach (var row in Entries)
            row.MarkSaved();
        NotifySavedStateChanged();
    }

    [RelayCommand(CanExecute = nameof(HasUnsavedChanges))]
    private void Revert()
    {
        foreach (var row in Entries)
            if (_savedEntries.TryGetValue(row.Number, out var entry))
                row.Apply(entry);
        NotifySavedStateChanged();
    }

    [RelayCommand]
    private void AutoRecognize()
    {
        if (IsRecognizing)
            return;

        var taskQueue = Instances.InstanceTabBarViewModel.ActiveTab?.TaskQueueViewModel;
        if (taskQueue == null)
        {
            ToastHelper.Warn("自动识别", "没有可用的游戏实例。");
            return;
        }

        IsRecognizing = true;
        try
        {
            if (taskQueue.Processor.MaaTasker is not { IsInitialized: true })
                taskQueue.Processor.TaskQueue.Enqueue(new MFATask
                {
                    Name = "刀帐识别前启动",
                    Type = MFATask.MFATaskType.MFA,
                    Action = async () => await taskQueue.Processor.TestConnecting(),
                    OwnerViewModel = taskQueue,
                });

            taskQueue.Processor.TaskQueue.Enqueue(new MFATask
            {
                Name = "刀帐自动识别",
                Type = MFATask.MFATaskType.MAAFW,
                OwnerViewModel = taskQueue,
                Action = async () =>
                {
                    try
                    {
                        var tasker = taskQueue.Processor.MaaTasker;
                        if (tasker is not { IsInitialized: true })
                            throw new InvalidOperationException("刀帐识别前连接失败，请检查模拟器和游戏窗口。");
                        var job = tasker.AppendTask(new MaaNode { Name = "SwordBookScan" });
                        if (job.WaitFor(MaaJobStatus.Succeeded) == null)
                        {
                            await ShowRecognitionFailureAsync();
                            return;
                        }
                        await DispatcherHelper.RunOnMainThreadAsync(LoadDraft);
                    }
                    catch (Exception exception)
                    {
                        LoggerHelper.Warning($"[刀帐] 自动识别任务失败：{exception.Message}");
                        await ShowRecognitionFailureAsync();
                    }
                    finally
                    {
                        await DispatcherHelper.RunOnMainThreadAsync(() => IsRecognizing = false);
                    }
                },
            });
            taskQueue.Processor.Start(true, checkUpdate: false);
        }
        catch
        {
            IsRecognizing = false;
            throw;
        }
    }

    [RelayCommand]
    private async Task Clear()
    {
        var result = await SukiMessageBox.ShowDialog(new SukiMessageBoxHost
        {
            Content = "确定要清空刀帐中的所有勾选状态吗？",
            ActionButtonsPreset = SukiMessageBoxButtons.YesNo,
            IconPreset = SukiMessageBoxIcons.Warning,
        }, new SukiMessageBoxOptions
        {
            Title = "清空刀帐",
        });

        if (!result.Equals(SukiMessageBoxResult.Yes))
            return;

        foreach (var row in Entries)
            row.ClearChecks();
        NotifySavedStateChanged();
    }

    /// <summary>勾选全部刀种，等同于不限制刀种。</summary>
    [RelayCommand]
    private void SelectAllTypes()
    {
        foreach (var option in TypeFilterOptions)
            option.IsSelected = true;
    }

    /// <summary>取消全部刀种勾选，等同于不限制刀种。</summary>
    [RelayCommand]
    private void ClearTypes()
    {
        foreach (var option in TypeFilterOptions)
            option.IsSelected = false;
    }

    partial void OnIsRecognizingChanged(bool value) => OnPropertyChanged(nameof(IsIdle));
    partial void OnOwnedFilterChanged(SwordBookFilterOption value) => ApplyFilters();
    partial void OnWoundedFilterChanged(SwordBookFilterOption value) => ApplyFilters();
    partial void OnTrueSwordFilterChanged(SwordBookFilterOption value) => ApplyFilters();
    partial void OnInnerCareFilterChanged(SwordBookFilterOption value) => ApplyFilters();
    partial void OnCasualFilterChanged(SwordBookFilterOption value) => ApplyFilters();

    private static Task ShowRecognitionFailureAsync()
    {
        return DispatcherHelper.RunOnMainThreadAsync(() =>
        {
            _ = SukiMessageBox.ShowDialog(new SukiMessageBoxHost
            {
                Content = "请先将游戏页面切换至具体刀剑男士的刀帐页面，并确保顶部同时可识别到“序号”和数字，然后重新点击“自动识别”。",
                ActionButtonsPreset = SukiMessageBoxButtons.OK,
                IconPreset = SukiMessageBoxIcons.Warning,
            }, new SukiMessageBoxOptions
            {
                Title = "刀帐自动识别失败",
            });
        });
    }

    private void LoadCatalog()
    {
        var path = Path.Combine(AppPaths.ResourceDirectory, "base", "SwordBookCatalog.json");
        if (!File.Exists(path))
            return;
        var catalog = (JsonConvert.DeserializeObject<List<SwordBookCatalogItem>>(File.ReadAllText(path)) ?? [])
            .Where(item => !item.TypeOnly)
            .ToList();
        var duplicateNames = catalog.GroupBy(item => item.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .ToDictionary(group => group.Key, group => group.Last().Number, StringComparer.Ordinal);
        foreach (var item in catalog)
        {
            var displayName = duplicateNames.TryGetValue(item.Name, out var lastNumber) && lastNumber == item.Number
                ? $"{item.Name}·极"
                : item.Name;
            Entries.Add(new SwordBookRowViewModel(item.Number, item.Type, displayName, OnRowChanged));
        }
        // 刀种筛选项按固定顺序排列，数量同样是全量统计。
        var types = Entries.Select(row => row.Type)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(type =>
            {
                var index = Array.IndexOf(TypeOrder, type);
                return index < 0 ? int.MaxValue : index;
            });
        foreach (var type in types)
            TypeFilterOptions.Add(new SwordBookTypeFilterOption(type, Entries.Count(row => row.Type == type), ApplyFilters));
        ApplyFilters();
    }

    private void LoadSavedState()
    {
        var saved = ConfigurationManager.Current.GetValue<List<SwordBookPortraitState>>(ConfigurationKeys.SwordBookEntries, []);
        var savedByNumber = saved.ToDictionary(item => item.Number, StringComparer.Ordinal);
        _savedEntries.Clear();
        foreach (var row in Entries)
        {
            if (savedByNumber.TryGetValue(row.Number, out var state))
            {
                row.Apply(state);
                row.MarkSaved();
            }
            else
            {
                row.ClearChecks();
                row.MarkSaved();
            }
            _savedEntries[row.Number] = ToEntry(row).Clone();
        }
        NotifySavedStateChanged();
    }

    private void OnSwordBookDataSaved()
    {
        _ = DispatcherHelper.RunOnMainThreadAsync(LoadSavedState);
    }

    private void LoadDraft()
    {
        if (!File.Exists(DraftPath))
            return;
        var draft = JsonConvert.DeserializeObject<List<SwordBookPortraitState>>(File.ReadAllText(DraftPath)) ?? [];
        var draftByNumber = draft.ToDictionary(item => item.Number, StringComparer.Ordinal);
        foreach (var row in Entries)
            if (draftByNumber.TryGetValue(row.Number, out var state))
                row.Apply(state);
        NotifySavedStateChanged();
    }

    private void OnRowChanged()
    {
        ApplyFilters();
        NotifySavedStateChanged();
    }

    private void ApplyFilters()
    {
        UpdateFilterCounts();
        UpdateColumnActiveStates();
        var types = SelectedTypes();
        HasTypeFilter = types is not null && types.Count < TypeFilterOptions.Count;
        VisibleEntries.Clear();
        foreach (var row in Entries)
            if ((types is null || types.Contains(row.Type))
                && SwordBookFilterMatcher.Matches(row.Owned, FilterOf(OwnedFilter))
                && SwordBookFilterMatcher.Matches(row.Wounded, FilterOf(WoundedFilter))
                && SwordBookFilterMatcher.Matches(row.TrueSword, FilterOf(TrueSwordFilter))
                && SwordBookFilterMatcher.Matches(row.InnerCare, FilterOf(InnerCareFilter))
                && SwordBookFilterMatcher.Matches(row.Casual, FilterOf(CasualFilter)))
                VisibleEntries.Add(row);
    }

    /// <summary>已勾选的刀种；一个都没勾时返回 null，表示不限刀种。</summary>
    private HashSet<string>? SelectedTypes()
    {
        HashSet<string>? types = null;
        foreach (var option in TypeFilterOptions)
            if (option.IsSelected)
                (types ??= new HashSet<string>(StringComparer.Ordinal)).Add(option.Type);
        return types;
    }

    /// <summary>刷新五个筛选下拉的数量：按全量统计，不随其它列的筛选变化。</summary>
    private void UpdateFilterCounts()
    {
        var total = Entries.Count;
        UpdateColumnCounts(OwnedFilterOptions, total, Entries.Count(row => row.Owned));
        UpdateColumnCounts(WoundedFilterOptions, total, Entries.Count(row => row.Wounded));
        UpdateColumnCounts(TrueSwordFilterOptions, total, Entries.Count(row => row.TrueSword));
        UpdateColumnCounts(InnerCareFilterOptions, total, Entries.Count(row => row.InnerCare));
        UpdateColumnCounts(CasualFilterOptions, total, Entries.Count(row => row.Casual));
    }

    /// <summary>写入单列三个下拉项的数量：全部、已拥有、未拥有。</summary>
    private static void UpdateColumnCounts(IReadOnlyList<SwordBookFilterOption> options, int total, int owned)
    {
        foreach (var option in options)
            option.Count = option.Filter switch
            {
                SwordBookFilter.Owned => owned,
                SwordBookFilter.Unowned => total - owned,
                _ => total,
            };
    }

    /// <summary>创建一组筛选下拉项，默认选中「全部」。</summary>
    private static IReadOnlyList<SwordBookFilterOption> CreateFilterOptions(string columnName) =>
    [
        new SwordBookFilterOption(SwordBookFilter.All, "全部", columnName),
        new SwordBookFilterOption(SwordBookFilter.Owned, "已拥有", columnName),
        new SwordBookFilterOption(SwordBookFilter.Unowned, "未拥有", columnName),
    ];

    /// <summary>标记各列是否处于筛选状态，用于高亮列名。</summary>
    private void UpdateColumnActiveStates()
    {
        MarkColumn(OwnedFilterOptions, OwnedFilter);
        MarkColumn(WoundedFilterOptions, WoundedFilter);
        MarkColumn(TrueSwordFilterOptions, TrueSwordFilter);
        MarkColumn(InnerCareFilterOptions, InnerCareFilter);
        MarkColumn(CasualFilterOptions, CasualFilter);
    }

    /// <summary>把某一列的筛选状态写到该列全部下拉项上，供列名高亮使用。</summary>
    private static void MarkColumn(IReadOnlyList<SwordBookFilterOption> options, SwordBookFilterOption? selected)
    {
        var isFiltered = selected is not null && selected.Filter != SwordBookFilter.All;
        foreach (var option in options)
            option.IsColumnFiltered = isFiltered;
    }

    /// <summary>读取下拉项对应的筛选状态；下拉尚未选中时按全部处理。</summary>
    private static SwordBookFilter FilterOf(SwordBookFilterOption? option) => option?.Filter ?? SwordBookFilter.All;

    private void NotifySavedStateChanged()
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        SaveCommand.NotifyCanExecuteChanged();
        RevertCommand.NotifyCanExecuteChanged();
    }
    private static bool HasChanged(SwordBookRowViewModel row) =>
        row.Owned != row.SavedOwned || row.Wounded != row.SavedWounded || row.TrueSword != row.SavedTrueSword ||
        row.InnerCare != row.SavedInnerCare || row.Casual != row.SavedCasual;

    private static SwordBookEntry ToEntry(SwordBookRowViewModel row) => new(row.Number, row.Type, row.Name)
    {
        Owned = row.Owned,
        Wounded = row.Wounded, TrueSword = row.TrueSword, InnerCare = row.InnerCare, Casual = row.Casual,
    };

    private static SwordBookPortraitState ToState(SwordBookEntry entry) =>
        new(entry.Number, entry.Owned, entry.Wounded, entry.TrueSword, entry.InnerCare, entry.Casual);

    private sealed record SwordBookCatalogItem(string Number, string Type, string Name, bool TypeOnly = false);
}

public sealed partial class SwordBookRowViewModel : ObservableObject
{
    private readonly Action _changed;
    public SwordBookRowViewModel(string number, string type, string name, Action changed)
    {
        Number = number; Type = type; Name = name; _changed = changed;
    }
    public string Number { get; }
    public string Type { get; }
    public string Name { get; }
    public bool SavedWounded { get; private set; }
    public bool SavedOwned { get; private set; }
    public bool SavedTrueSword { get; private set; }
    public bool SavedInnerCare { get; private set; }
    public bool SavedCasual { get; private set; }
    [ObservableProperty] private bool _wounded;
    [ObservableProperty] private bool _owned;
    [ObservableProperty] private bool _trueSword;
    [ObservableProperty] private bool _innerCare;
    [ObservableProperty] private bool _casual;
    partial void OnWoundedChanged(bool value) => _changed();
    partial void OnOwnedChanged(bool value) => _changed();
    partial void OnTrueSwordChanged(bool value) => _changed();
    partial void OnInnerCareChanged(bool value) => _changed();
    partial void OnCasualChanged(bool value) => _changed();

    public void Apply(SwordBookEntry entry)
    {
        Owned = entry.Owned; Wounded = entry.Wounded; TrueSword = entry.TrueSword; InnerCare = entry.InnerCare; Casual = entry.Casual;
        MarkSaved();
    }
    public void Apply(SwordBookPortraitState state)
    {
        Owned = state.Owned; Wounded = state.Wounded; TrueSword = state.TrueSword; InnerCare = state.InnerCare; Casual = state.Casual;
    }
    public void MarkSaved()
    {
        SavedOwned = Owned; SavedWounded = Wounded; SavedTrueSword = TrueSword; SavedInnerCare = InnerCare; SavedCasual = Casual;
    }

    public void ClearChecks()
    {
        Owned = false; Wounded = false; TrueSword = false; InnerCare = false; Casual = false;
    }
}

/// <summary>刀帐筛选下拉项，携带该选项在整本刀帐中的统计数量。</summary>
public sealed partial class SwordBookFilterOption : ObservableObject
{
    public SwordBookFilterOption(SwordBookFilter filter, string label, string columnName)
    {
        Filter = filter;
        Label = label;
        ColumnName = columnName;
    }

    /// <summary>该选项对应的筛选状态。</summary>
    public SwordBookFilter Filter { get; }

    /// <summary>选项名称：全部、已拥有或未拥有。</summary>
    public string Label { get; }

    /// <summary>该下拉所属的列名，收起时显示：已拥有、中伤、真剑、内番或轻装。</summary>
    public string ColumnName { get; }

    /// <summary>该选项在整本刀帐中的数量。</summary>
    [ObservableProperty] private int _count;

    /// <summary>数量的显示文本。</summary>
    public string CountText => Count.ToString();

    /// <summary>所属列是否已应用「已拥有」或「未拥有」筛选，用于高亮列名。</summary>
    [ObservableProperty] private bool _isColumnFiltered;

    partial void OnCountChanged(int value) => OnPropertyChanged(nameof(CountText));
}

/// <summary>刀种筛选项，可同时勾选多个。</summary>
public sealed partial class SwordBookTypeFilterOption : ObservableObject
{
    private readonly Action _changed;

    public SwordBookTypeFilterOption(string type, int count, Action changed)
    {
        Type = type;
        Count = count;
        _changed = changed;
    }

    /// <summary>刀种名称。</summary>
    public string Type { get; }

    /// <summary>该刀种在整本刀帐中的数量。</summary>
    public int Count { get; }

    /// <summary>数量的显示文本。</summary>
    public string CountText => Count.ToString();

    /// <summary>是否勾选该刀种。</summary>
    [ObservableProperty] private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => _changed();
}

public sealed record SwordBookPortraitState(string Number, bool Owned, bool Wounded, bool TrueSword, bool InnerCare, bool Casual);
