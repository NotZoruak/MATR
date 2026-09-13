using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MFAAvalonia.Helper;
using MFAAvalonia.Models;
using MFAAvalonia.Services;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.ViewModels.UsersControls;
using SukiUI.Controls;
using SukiUI.Dialogs;
using SukiUI.MessageBox;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MFAAvalonia.ViewModels.Pages;

/// <summary>工作记录页：解析日志并展示运行记录</summary>
public partial class WorkRecordsViewModel : ViewModelBase
{
    private string SavedRecordsPath => Path.Combine(AppPaths.ConfigDirectory, "saved_work_records.json");

    /// <summary>运行记录列表（时间倒序）</summary>
    [ObservableProperty] private ObservableCollection<WorkRecord> _records = [];

    /// <summary>长期保存的工作记录。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSavedRecords))]
    private ObservableCollection<SavedWorkRecord> _savedRecords = [];

    /// <summary>当前从日志记录中选中的项目。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveSelectedRecords))]
    [NotifyPropertyChangedFor(nameof(ShowSelectedStatus))]
    private ObservableCollection<WorkRecord> _selectedLogRecords = [];

    /// <summary>当前从已保存记录中选中的项目。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMergeSavedRecords))]
    [NotifyPropertyChangedFor(nameof(CanRenameSavedRecord))]
    [NotifyPropertyChangedFor(nameof(CanDeleteSavedRecords))]
    [NotifyPropertyChangedFor(nameof(SelectedTimeText))]
    [NotifyPropertyChangedFor(nameof(ShowSelectedStatus))]
    private ObservableCollection<SavedWorkRecord> _selectedSavedRecords = [];

    /// <summary>当前选中记录</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedDurationText))]
    [NotifyPropertyChangedFor(nameof(SelectedResourcesText))]
    [NotifyPropertyChangedFor(nameof(SelectedResourceItems))]
    [NotifyPropertyChangedFor(nameof(SelectedHasResources))]
    [NotifyPropertyChangedFor(nameof(SelectedSwordDropsText))]
    [NotifyPropertyChangedFor(nameof(SelectedHasSwordDrops))]
    [NotifyPropertyChangedFor(nameof(SelectedSwordDropGroups))]
    [NotifyPropertyChangedFor(nameof(SelectedHasOutcomes))]
    [NotifyPropertyChangedFor(nameof(SelectedLogisticsSummaryText))]
    [NotifyPropertyChangedFor(nameof(SelectedLogisticsDispatchCountText))]
    [NotifyPropertyChangedFor(nameof(SelectedHasLogisticsSummary))]
    [NotifyPropertyChangedFor(nameof(SelectedHasLogisticsDispatch))]
    [NotifyPropertyChangedFor(nameof(SelectedLogisticsDispatchTexts))]
    [NotifyPropertyChangedFor(nameof(SelectedHasLogisticsRepairs))]
    [NotifyPropertyChangedFor(nameof(SelectedLogisticsRepairTexts))]
    [NotifyPropertyChangedFor(nameof(SelectedHasLogisticsNaibanOutfits))]
    [NotifyPropertyChangedFor(nameof(SelectedLogisticsNaibanOutfitTexts))]
    [NotifyPropertyChangedFor(nameof(SelectedHasLogistics))]
    [NotifyPropertyChangedFor(nameof(SelectedSpecialEventsText))]
    [NotifyPropertyChangedFor(nameof(SelectedHasSpecialEvents))]
    [NotifyPropertyChangedFor(nameof(SelectedHasReturnHome))]
    [NotifyPropertyChangedFor(nameof(SelectedHasFlowerBrush))]
    [NotifyPropertyChangedFor(nameof(SelectedHasSortie))]
    [NotifyPropertyChangedFor(nameof(SelectedHasRound))]
    [NotifyPropertyChangedFor(nameof(SelectedTimeText))]
    [NotifyPropertyChangedFor(nameof(SelectedConfigSourceText))]
    private WorkRecord? _selectedRecord;

    /// <summary>多选时显示的提示或合并预览。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectionSummary))]
    private string _selectionSummary = "";

    /// <summary>任务概况卡片内容是否展开。</summary>
    [ObservableProperty] private bool _isTaskSummaryExpanded = true;

    /// <summary>任务收获卡片内容是否展开。</summary>
    [ObservableProperty] private bool _isTaskOutcomeExpanded = true;

    /// <summary>后勤记录卡片内容是否展开。</summary>
    [ObservableProperty] private bool _isLogisticsExpanded = true;

    /// <summary>特殊情况卡片内容是否展开。</summary>
    [ObservableProperty] private bool _isSpecialEventsExpanded = true;

    /// <summary>收起或展开任务概况卡片内容。</summary>
    [RelayCommand]
    private void ToggleTaskSummary() => IsTaskSummaryExpanded = !IsTaskSummaryExpanded;

    /// <summary>收起或展开任务收获卡片内容。</summary>
    [RelayCommand]
    private void ToggleTaskOutcome() => IsTaskOutcomeExpanded = !IsTaskOutcomeExpanded;

    /// <summary>收起或展开后勤记录卡片内容。</summary>
    [RelayCommand]
    private void ToggleLogistics() => IsLogisticsExpanded = !IsLogisticsExpanded;

    /// <summary>收起或展开特殊情况卡片内容。</summary>
    [RelayCommand]
    private void ToggleSpecialEvents() => IsSpecialEventsExpanded = !IsSpecialEventsExpanded;

    public bool HasSavedRecords => SavedRecords.Count > 0;
    public bool CanSaveSelectedRecords => SelectedLogRecords.Count > 0
        && SelectedLogRecords.Select(record => record.TaskName).Distinct(StringComparer.Ordinal).Count() == 1
        && SelectedLogRecords.All(record => record.Status != "进行中");
    public bool CanMergeSavedRecords => SelectedSavedRecords.Count > 1
        && SelectedSavedRecords.Select(record => record.TaskName).Distinct(StringComparer.Ordinal).Count() == 1;
    public bool CanRenameSavedRecord => SelectedSavedRecords.Count == 1;
    public bool CanDeleteSavedRecords => SelectedSavedRecords.Count > 0;
    public bool ShowSelectedStatus => SelectedSavedRecords.Count == 0;
    public bool HasSelectionSummary => !string.IsNullOrWhiteSpace(SelectionSummary);

    /// <summary>刀种展示顺序</summary>
    private static readonly string[] TypeOrder = WorkRecordBuilder.SwordTypeOrder;

    /// <summary>资源展示顺序</summary>
    private static readonly string[] ResourceOrder =
        ["木炭", "玉钢", "冷却材", "砥石", "委托符", "加速符", "小判", "小判箱"];

    /// <summary>刷新：重新扫描全部日志并重建列表</summary>
    [RelayCommand]
    private void Refresh()
    {
        try
        {
            var entries = new List<LogEntry>();
            var dir = AppPaths.LogsDirectory;
            if (Directory.Exists(dir))
            {
                foreach (var file in Directory.GetFiles(dir, "log-*.log").OrderBy(f => f))
                    entries.AddRange(LogParser.ParseFile(file));
            }

            var records = WorkRecordBuilder.Build(entries);
            // 时间倒序
            var orderedRecords = records.OrderByDescending(r => r.StartTime).ToList();
            ApplyConfigDisplayNames(orderedRecords);
            Records = new ObservableCollection<WorkRecord>(orderedRecords);
            var savedRecords = SavedWorkRecordStore.Load(SavedRecordsPath);
            ApplyConfigDisplayNames(savedRecords);
            SavedRecords = new ObservableCollection<SavedWorkRecord>(savedRecords);
            SetSelectedLogRecords(Records.Take(1));
        }
        catch (Exception ex)
        {
            // 解析失败不影响页面可用性：记录错误并保留现有列表
            LoggerHelper.Error($"工作记录刷新失败：{ex.Message}");
        }
    }

    /// <summary>根据实例 ID 动态取得当前页签名称。</summary>
    private static void ApplyConfigDisplayNames(IEnumerable<WorkRecord> records)
    {
        foreach (var record in records)
            record.DisplayConfigName = ResolveConfigDisplayName(record.ConfigName);
    }

    /// <summary>根据实例 ID 动态取得当前页签名称。</summary>
    private static void ApplyConfigDisplayNames(IEnumerable<SavedWorkRecord> records)
    {
        foreach (var record in records)
            record.DisplayConfigName = ResolveConfigDisplayName(record.ConfigName);
    }

    private static string ResolveConfigDisplayName(string configId)
    {
        if (string.IsNullOrWhiteSpace(configId))
            return "";

        var instance = MaaProcessorManager.Instance.GetAllInstanceIdsAndNames()
            .FirstOrDefault(item => string.Equals(item.Id, configId, StringComparison.Ordinal));
        return string.IsNullOrWhiteSpace(instance.Name) ? configId : instance.Name;
    }

    /// <summary>由页面同步日志记录的多选状态。</summary>
    public void SetSelectedLogRecords(IEnumerable<WorkRecord> records)
    {
        SelectedSavedRecords.Clear();
        SelectedLogRecords = new ObservableCollection<WorkRecord>(records);
        if (SelectedLogRecords.Count == 1)
        {
            SelectedRecord = SelectedLogRecords[0];
            SelectionSummary = "";
        }
        else if (SelectedLogRecords.Count > 1 && CanSaveSelectedRecords)
        {
            SelectedRecord = SavedWorkRecordService.Merge(SelectedLogRecords, "").ToWorkRecord();
            SelectionSummary = $"已选择 {SelectedLogRecords.Count} 条记录，可合并保存";
        }
        else
        {
            SelectedRecord = null;
            SelectionSummary = BuildLogMergeFailureSummary();
        }
        NotifySelectionCommands();
    }

    /// <summary>生成日志记录无法合并时的具体原因。</summary>
    private string BuildLogMergeFailureSummary()
    {
        if (SelectedLogRecords.Count <= 1)
            return "";

        var reasons = new List<string>();
        if (SelectedLogRecords.Any(record => record.Status == "进行中"))
            reasons.Add("进行中的任务无法合并");
        if (SelectedLogRecords.Select(record => record.TaskName)
            .Distinct(StringComparer.Ordinal).Count() > 1)
            reasons.Add("不同任务无法合并");

        if (reasons.Count == 0)
            reasons.Add("当前记录无法合并");

        return $"已选择 {SelectedLogRecords.Count} 条记录，{string.Join("或", reasons)}";
    }

    /// <summary>由页面同步已保存记录的多选状态。</summary>
    public void SetSelectedSavedRecords(IEnumerable<SavedWorkRecord> records)
    {
        SelectedLogRecords.Clear();
        SelectedSavedRecords = new ObservableCollection<SavedWorkRecord>(records);
        if (SelectedSavedRecords.Count == 1)
        {
            SelectedRecord = SelectedSavedRecords[0].ToWorkRecord();
            SelectionSummary = "";
        }
        else if (SelectedSavedRecords.Count > 1 && CanMergeSavedRecords)
        {
            SelectedRecord = SavedWorkRecordService.Merge(
                SelectedSavedRecords, "").ToWorkRecord();
            SelectionSummary = $"已选择 {SelectedSavedRecords.Count} 条已保存记录，可合并";
        }
        else
        {
            SelectedRecord = null;
            SelectionSummary = SelectedSavedRecords.Count > 1
                ? $"已选择 {SelectedSavedRecords.Count} 条不同任务，无法合并"
                : "";
        }
        NotifySelectionCommands();
    }

    private void NotifySelectionCommands()
    {
        OnPropertyChanged(nameof(CanSaveSelectedRecords));
        OnPropertyChanged(nameof(CanMergeSavedRecords));
        OnPropertyChanged(nameof(CanRenameSavedRecord));
        OnPropertyChanged(nameof(CanDeleteSavedRecords));
        SaveCommand.NotifyCanExecuteChanged();
        MergeSavedCommand.NotifyCanExecuteChanged();
        RenameSavedCommand.NotifyCanExecuteChanged();
        DeleteSavedCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSaveSelectedRecords))]
    private void Save()
    {
        ShowNameDialog("保存工作记录", name =>
        {
            var uniqueName = SavedWorkRecordService.CreateUniqueName(name, SavedRecords.Select(record => record.DisplayName));
            var saved = SelectedLogRecords.Count == 1
                ? SavedWorkRecordService.Save(SelectedLogRecords[0], uniqueName)
                : SavedWorkRecordService.Merge(SelectedLogRecords, uniqueName);
            SavedRecords.Add(saved);
            PersistSavedRecords();
            NotifySavedStateChanged();
        });
    }

    [RelayCommand(CanExecute = nameof(CanMergeSavedRecords))]
    private void MergeSaved()
    {
        ShowNameDialog("合并保存记录", name =>
        {
            var uniqueName = SavedWorkRecordService.CreateUniqueName(name, SavedRecords.Select(record => record.DisplayName));
            var merged = SavedWorkRecordService.Merge(
                SelectedSavedRecords, uniqueName);
            foreach (var record in SelectedSavedRecords.ToList())
                SavedRecords.Remove(record);
            SavedRecords.Add(merged);
            PersistSavedRecords();
            NotifySavedStateChanged();
            SetSelectedSavedRecords([merged]);
        });
    }

    [RelayCommand(CanExecute = nameof(CanRenameSavedRecord))]
    private void RenameSaved()
    {
        var record = SelectedSavedRecords[0];
        ShowNameDialog("重命名工作记录", name =>
        {
            var existingNames = SavedRecords
                .Where(item => !ReferenceEquals(item, record))
                .Select(item => item.DisplayName);
            record.DisplayName = SavedWorkRecordService.CreateUniqueName(name, existingNames);

            var index = SavedRecords.IndexOf(record);
            if (index >= 0)
                SavedRecords[index] = record;
            PersistSavedRecords();
        }, record.DisplayName, "请输入新的记录名称");
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSavedRecords))]
    private async Task DeleteSaved()
    {
        var result = await SukiMessageBox.ShowDialog(new SukiMessageBoxHost
        {
            Content = $"确定要删除选中的 {SelectedSavedRecords.Count} 条已保存记录吗？",
            ActionButtonsPreset = SukiMessageBoxButtons.YesNo,
            IconPreset = SukiMessageBoxIcons.Warning,
        }, new SukiMessageBoxOptions { Title = "删除保存记录" });
        if (result is not SukiMessageBoxResult.Yes)
            return;

        foreach (var record in SelectedSavedRecords.ToList())
            SavedRecords.Remove(record);
        PersistSavedRecords();
        NotifySavedStateChanged();
        SetSelectedSavedRecords([]);
    }

    [RelayCommand(CanExecute = nameof(HasSavedRecords))]
    private async Task ClearSaved()
    {
        var result = await SukiMessageBox.ShowDialog(new SukiMessageBoxHost
        {
            Content = "确定要清空全部已保存记录吗？此操作不可撤销。",
            ActionButtonsPreset = SukiMessageBoxButtons.YesNo,
            IconPreset = SukiMessageBoxIcons.Warning,
        }, new SukiMessageBoxOptions { Title = "清空保存记录" });
        if (result is not SukiMessageBoxResult.Yes)
            return;

        SavedRecords.Clear();
        PersistSavedRecords();
        NotifySavedStateChanged();
        SetSelectedSavedRecords([]);
    }

    private void PersistSavedRecords() => SavedWorkRecordStore.Save(SavedRecordsPath, SavedRecords);

    private void NotifySavedStateChanged()
    {
        OnPropertyChanged(nameof(HasSavedRecords));
        ClearSavedCommand.NotifyCanExecuteChanged();
    }

    private void ShowNameDialog(
        string title,
        Action<string> onConfirmed,
        string initialName = "",
        string prompt = "请输入保存记录名称")
    {
        Instances.DialogManager.CreateDialog()
            .WithTitle(title)
            .WithViewModel(dialog => new WorkRecordNameDialogViewModel(
                dialog, onConfirmed, initialName, prompt))
            .TryShow();
    }

    // ---------- 选中记录卡片展示字段 ----------

    /// <summary>是否有返回本丸记录（0 次不显示该行）</summary>
    public bool SelectedHasReturnHome => SelectedRecord?.ReturnHomeCount > 0;

    /// <summary>是否有出阵记录（0 次不显示该项）</summary>
    public bool SelectedHasSortie => SelectedRecord?.SortieCount > 0;

    /// <summary>是否有完成圈数记录（0 次不显示该项）</summary>
    public bool SelectedHasRound => SelectedRecord?.RoundCount > 0;

    /// <summary>是否有出阵刷花（0 次不显示该行）</summary>
    public bool SelectedHasFlowerBrush => SelectedRecord?.FlowerBrushCount > 0;

    /// <summary>耗时文本：1 小时 27 分 / 35 分钟</summary>
    public string SelectedDurationText => SelectedRecord?.DurationText ?? "";

    /// <summary>选中记录的配置来源文本；多选时（合并预览无单一来源）不显示。</summary>
    public string SelectedConfigSourceText
    {
        get
        {
            if (SelectedSavedRecords.Count > 0
                || SelectedLogRecords.Count > 1
                || SelectedSavedRecords.Count > 1)
                return "";
            return ResolveConfigDisplayName(SelectedRecord?.ConfigName ?? "");
        }
    }

    /// <summary>详情中的时间文本，已保存记录只显示日期范围。</summary>
    public string SelectedTimeText
    {
        get
        {
            if (SelectedRecord is null)
                return "";
            if (SelectedLogRecords.Count > 1 || SelectedSavedRecords.Count > 1)
                return SelectedRecord.DurationText;
            if (SelectedSavedRecords.Count > 0)
            {
                var start = SelectedRecord.StartTime.ToString("yyyy-MM-dd");
                var end = SelectedRecord.EndTime.ToString("yyyy-MM-dd");
                return $"{start}—{end} · {SelectedRecord.DurationText}";
            }
            return SelectedRecord.ListTimeText;
        }
    }

    /// <summary>资源收获文本：木炭x240 玉钢x60 小判箱x3</summary>
    public string SelectedResourcesText =>
        SelectedRecord is null
            ? ""
            : string.Join("  ", SelectedRecord.ResourceGains
                .OrderBy(kv => Array.IndexOf(ResourceOrder, kv.Key) is var i && i < 0 ? int.MaxValue : i)
                .Select(kv => $"{kv.Key}x{kv.Value}"));

    /// <summary>资源收获条目，供窄宽度下按完整条目自动换行显示</summary>
    public IReadOnlyList<string> SelectedResourceItems =>
        SelectedRecord is null
            ? []
            : SelectedRecord.ResourceGains
                .OrderBy(kv => Array.IndexOf(ResourceOrder, kv.Key) is var i && i < 0 ? int.MaxValue : i)
                .Select(kv => $"{kv.Key}x{kv.Value}")
                .ToList();

    /// <summary>是否有资源收获</summary>
    public bool SelectedHasResources => SelectedRecord?.ResourceGains.Count > 0;

    /// <summary>刀剑掉落分组文本（短胁打太大太枪薙剑序，每组一行）</summary>
    public string SelectedSwordDropsText
    {
        get
        {
            if (SelectedRecord is null)
                return "";
            var sb = new StringBuilder();
            foreach (var group in SelectedRecord.SwordDrops
                         .GroupBy(d => d.SwordType)
                         .OrderBy(g => Array.IndexOf(TypeOrder, g.Key) is var i && i < 0 ? int.MaxValue : i))
            {
                sb.AppendLine($"{group.Key}:{string.Join("，", group
                    .GroupBy(d => d.SwordName)
                    .Select(g => $"{g.Key}（x{g.Count()}）"))}");
            }
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>刀剑掉落分组展示数据</summary>
    public IReadOnlyList<SwordDropGroupDisplay> SelectedSwordDropGroups
    {
        get
        {
            if (SelectedRecord is null)
                return [];

            return SelectedRecord.SwordDrops
                .GroupBy(d => d.SwordType)
                .OrderBy(g => Array.IndexOf(TypeOrder, g.Key) is var i && i < 0 ? int.MaxValue : i)
                .Select(g => new SwordDropGroupDisplay(
                    g.Key,
                    string.Join("，", g.GroupBy(d => d.SwordName)
                        .Select(nameGroup => $"{nameGroup.Key}（x{nameGroup.Count()}）"))))
                .ToList();
        }
    }

    /// <summary>是否有刀剑掉落</summary>
    public bool SelectedHasSwordDrops => SelectedRecord?.SwordDrops.Count > 0;

    /// <summary>是否有出阵收获</summary>
    public bool SelectedHasOutcomes => SelectedHasResources || SelectedHasSwordDrops;

    /// <summary>后勤摘要文本：倒计时结束、检查队伍状况和刷花属于同一组。</summary>
    public string SelectedLogisticsSummaryText
    {
        get
        {
            if (SelectedRecord is null)
                return "";

            var inlineKeys = new[] { "倒计时结束", "检查队伍状况", "刷花" };
            var inlineItems = inlineKeys
                .Where(key => SelectedRecord.LogisticsCounts.ContainsKey(key))
                .Select(key => $"{key} ×{SelectedRecord.LogisticsCounts[key]}")
                .ToList();
            return string.Join("    ", inlineItems);
        }
    }

    /// <summary>派遣远征摘要文本，与下面的详细派遣记录保持同一组。</summary>
    public string SelectedLogisticsDispatchCountText =>
        SelectedRecord?.LogisticsCounts.TryGetValue("派遣远征", out var count) == true
            ? $"派遣远征 ×{count}"
            : "";

    /// <summary>派遣远征明细文本，由界面按可用宽度自动换行。</summary>
    public IReadOnlyList<string> SelectedLogisticsDispatchTexts =>
        SelectedRecord?.LogisticsDispatches
            .Select(d => $"{d.Time:MM-dd HH:mm}  {d.Unit} → {d.Map}")
            .ToList() ?? [];

    /// <summary>是否有后勤摘要</summary>
    public bool SelectedHasLogisticsSummary => !string.IsNullOrWhiteSpace(SelectedLogisticsSummaryText);

    /// <summary>是否有派遣远征记录</summary>
    public bool SelectedHasLogisticsDispatch =>
        !string.IsNullOrWhiteSpace(SelectedLogisticsDispatchCountText)
        || SelectedLogisticsDispatchTexts.Count > 0;

    /// <summary>修刀明细，位于派遣远征组与内番服组之间。</summary>
    public IReadOnlyList<string> SelectedLogisticsRepairTexts =>
        SelectedRecord?.LogisticsRepairs
            .Select(item => $"{item.Time:MM-dd HH:mm}  开始修复 {item.SwordName} {FormatRepairCost(item.Wood)}/{FormatRepairCost(item.Steel)}/{FormatRepairCost(item.Coolant)}/{FormatRepairCost(item.Whetstone)}")
            .ToList() ?? [];

    private static string FormatRepairCost(int cost) => cost < 0 ? "未识别" : cost.ToString();

    /// <summary>是否有修刀记录。</summary>
    public bool SelectedHasLogisticsRepairs => SelectedLogisticsRepairTexts.Count > 0;

    /// <summary>内番派遣与内番服结果合并显示，紧跟派遣远征组之后显示。</summary>
    public IReadOnlyList<string> SelectedLogisticsNaibanOutfitTexts =>
        SelectedRecord?.LogisticsNaibanOutfits
            .Select(item => $"{item.Time:MM-dd HH:mm}  安排内番（内番服 {item.SwordName}）")
            .ToList() ?? [];

    /// <summary>是否有内番服识别记录。</summary>
    public bool SelectedHasLogisticsNaibanOutfits => SelectedLogisticsNaibanOutfitTexts.Count > 0;

    /// <summary>是否有后勤记录</summary>
    public bool SelectedHasLogistics => SelectedRecord?.LogisticsCounts.Count > 0;

    /// <summary>特殊情况文本</summary>
    public string SelectedSpecialEventsText =>
        SelectedRecord is null
            ? ""
            : string.Join("\n", SelectedRecord.SpecialEvents.Select(e => $"{e.Time:MM-dd HH:mm} {e.Description}"));

    /// <summary>是否有特殊情况</summary>
    public bool SelectedHasSpecialEvents => SelectedRecord?.SpecialEvents.Count > 0;
}

/// <summary>刀剑掉落按刀种分组后的展示数据</summary>
public sealed record SwordDropGroupDisplay(string SwordType, string DropsText);
