using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaaFramework.Binding;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.ValueType;
using MFAAvalonia.Models;
using MFAAvalonia.Services;
using SukiUI.Controls;
using SukiUI.Dialogs;
using SukiUI.MessageBox;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using System.Threading.Tasks;

namespace MFAAvalonia.ViewModels.Pages;

/// <summary>仓库页面，展示核心资源与其他物品。</summary>
public partial class WarehouseViewModel : ViewModelBase
{
    private static readonly (string Key, string Name, string Icon, int Maximum)[] CoreDefinitions =
    [
        ("木炭", "木炭", "木炭.jpg", 9999999),
        ("玉钢", "玉钢", "玉鋼.jpg", 9999999),
        ("冷却材", "冷却材", "冷却材.jpg", 9999999),
        ("砥石", "砥石", "砥石.jpg", 9999999),
        ("委托符", "委托符", "", 9999),
        ("加速符", "加速符", "", 9999),
        ("小判", "小判", "", 99999999),
    ];

    private readonly WarehouseDataEditor _editor;
    private readonly WarehouseComparisonResourceSelection _comparisonSelection = new();
    private double _chartDisplayWidth = 650;
    private List<WarehouseResourceSnapshot> _pendingResourceHistory = [];
    private bool _hasPendingChartChanges;
    private readonly System.Collections.Generic.Dictionary<string, int> _savedCore = new(StringComparer.Ordinal);
    private readonly System.Collections.Generic.Dictionary<string, int> _savedOther = new(StringComparer.Ordinal);
    private static readonly string DraftPath = Path.Combine(AppPaths.ConfigDirectory, "warehouse_scan.json");

    public ObservableCollection<WarehouseCoreResourceViewModel> CoreResources { get; } = [];
    public ObservableCollection<WarehouseCoreResourceViewModel> CoreIconResources { get; } = [];
    public ObservableCollection<WarehouseCoreResourceViewModel> CoreTextResources { get; } = [];
    public ObservableCollection<WarehouseOtherItemViewModel> OtherItems { get; } = [];
    public ObservableCollection<WarehouseComparisonResourceOption> ComparisonResources { get; } = [];
    public ObservableCollection<WarehouseChartViewModel> Charts { get; } = [];
    public WarehouseComparisonChartViewModel ComparisonChart { get; private set; } = null!;
    public bool HasOtherItems => OtherItems.Count > 0;
    public bool NoOtherItems => !HasOtherItems;
    public bool HasChartHistory => ComparisonChart?.HasHistory == true || Charts.Any(chart => chart.HasHistory);
    public bool NoChartHistory => !HasChartHistory;
    public bool HasUnsavedChanges => _pendingResourceHistory.Count > 0
        || _hasPendingChartChanges
        || CoreResources.Any(HasCoreChanged)
        || OtherItems.Any(HasOtherChanged);
    public bool IsIdle => !IsRecognizing;
    public WarehouseChartRange SelectedChartRange { get; private set; } = WarehouseChartRange.Last7Days;
    public bool IsLast24HoursSelected => SelectedChartRange == WarehouseChartRange.Last24Hours;
    public bool IsLast7DaysSelected => SelectedChartRange == WarehouseChartRange.Last7Days;
    public bool IsLast30DaysSelected => SelectedChartRange == WarehouseChartRange.Last30Days;
    public string Last24HoursButtonText => IsLast24HoursSelected ? "✓ 24小时" : "24小时";
    public string Last7DaysButtonText => IsLast7DaysSelected ? "✓ 7天" : "7天";
    public string Last30DaysButtonText => IsLast30DaysSelected ? "✓ 30天" : "30天";
    public string ChartZoomText => $"{ChartZoomPercent}%";
    public double ChartCanvasWidth => _chartDisplayWidth * ChartZoomPercent / 100d;
    public bool CanZoomChartIn => ChartZoomPercent < GetMaximumChartZoom();
    public bool CanZoomChartOut => ChartZoomPercent > 100;
    public string LastUpdatedText => DataLastUpdatedService.GetWarehouseLastUpdatedText();
    public string AutoRecognizeHint => LastUpdatedTimeFormatter.FormatHint(
        "使用自动识别时，请保证游戏页面右上角能识别到目录按钮。",
        LastUpdatedText);

    [ObservableProperty] private bool _isRecognizing;
    [ObservableProperty] private int _chartZoomPercent = 100;

    public WarehouseViewModel()
    {
        _editor = new WarehouseDataEditor(ConfigurationManager.Current.GetValue(ConfigurationKeys.WarehouseData, new WarehouseData()));
        foreach (var definition in CoreDefinitions)
        {
            var value = GetValue(_editor.Data.CoreResources, definition.Key);
            var resource = new WarehouseCoreResourceViewModel(definition.Key, definition.Name, LoadIcon(definition.Icon), definition.Maximum, value, OnDataChanged);
            CoreResources.Add(resource);
            if (resource.HasIcon)
                CoreIconResources.Add(resource);
            else
                CoreTextResources.Add(resource);
            _savedCore[definition.Key] = value;
        }

        foreach (var resourceName in WarehouseComparisonResourceSelection.AvailableResourceNames)
            ComparisonResources.Add(new WarehouseComparisonResourceOption(resourceName, true, ToggleComparisonResource));

        var normalizedOtherItems = WarehouseScanDraftService.NormalizeOtherItems(_editor.Data.OtherItems);
        _editor.Data.OtherItems = normalizedOtherItems;
        foreach (var pair in normalizedOtherItems)
        {
            if (pair.Value <= 0)
                continue;
            OtherItems.Add(new WarehouseOtherItemViewModel(pair.Key, pair.Value, OnDataChanged));
            _savedOther[pair.Key] = pair.Value;
        }
        OtherItems.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasOtherItems));
            OnPropertyChanged(nameof(NoOtherItems));
            OnPropertyChanged(nameof(HasUnsavedChanges));
        };
        UpdateDataPersistenceService.WarehouseDataSaved += OnWarehouseDataSaved;
        RebuildCharts();
    }

    [RelayCommand]
    private void Save()
    {
        var data = new WarehouseData();
        foreach (var item in CoreResources)
        {
            data.CoreResources[item.Name] = item.Count;
            _savedCore[item.Name] = item.Count;
        }
        _savedOther.Clear();
        foreach (var item in OtherItems)
        {
            if (!string.IsNullOrWhiteSpace(item.Name) && item.Count > 0)
            {
                data.OtherItems[item.Name] = item.Count;
                _savedOther[item.Name] = item.Count;
            }
        }
        data.ResourceHistory = [.. _editor.Data.ResourceHistory, .. _pendingResourceHistory.Select(CloneSnapshot)];
        ConfigurationManager.Current.SetValue(ConfigurationKeys.WarehouseData, data);
        DataLastUpdatedService.MarkWarehouseUpdated();
        _editor.LoadData(data);
        _editor.Save();
        _pendingResourceHistory.Clear();
        _hasPendingChartChanges = false;
        OnPropertyChanged(nameof(LastUpdatedText));
        OnPropertyChanged(nameof(AutoRecognizeHint));
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    [RelayCommand]
    private void Revert()
    {
        foreach (var item in CoreResources)
            item.Count = GetValue(_savedCore, item.Name);
        foreach (var item in OtherItems)
            item.Count = GetValue(_savedOther, item.Name);
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    [RelayCommand]
    private async Task Clear()
    {
        var result = await SukiMessageBox.ShowDialog(new SukiMessageBoxHost
        {
            Content = "确定要清空仓库中的所有识别数据吗？",
            ActionButtonsPreset = SukiMessageBoxButtons.YesNo,
            IconPreset = SukiMessageBoxIcons.Warning,
        }, new SukiMessageBoxOptions { Title = "清空仓库数据" });
        if (!result.Equals(SukiMessageBoxResult.Yes))
            return;

        foreach (var item in CoreResources)
            item.Count = 0;
        foreach (var item in OtherItems)
            item.Count = 0;
        OnPropertyChanged(nameof(HasUnsavedChanges));
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
            WarehouseScanDraftService.Clear(DraftPath);
            if (taskQueue.Processor.MaaTasker is not { IsInitialized: true })
                taskQueue.Processor.TaskQueue.Enqueue(new MFATask
                {
                    Name = "仓库识别前启动",
                    Type = MFATask.MFATaskType.MFA,
                    Action = async () => await taskQueue.Processor.TestConnecting(),
                    OwnerViewModel = taskQueue,
                });

            taskQueue.Processor.TaskQueue.Enqueue(new MFATask
            {
                Name = "仓库自动识别",
                Type = MFATask.MFATaskType.MAAFW,
                OwnerViewModel = taskQueue,
                Action = async () =>
                {
                    try
                    {
                        var tasker = taskQueue.Processor.MaaTasker;
                        if (tasker is not { IsInitialized: true })
                            throw new InvalidOperationException("仓库识别前连接失败，请检查模拟器和游戏窗口。");
                        var job = tasker.AppendTask(new MaaNode { Name = "Warehouse_Start" });
                        if (job.WaitFor(MaaJobStatus.Succeeded) == null)
                            throw new InvalidOperationException("仓库自动识别任务执行失败。");
                        await DispatcherHelper.RunOnMainThreadAsync(LoadDraft);
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

    partial void OnIsRecognizingChanged(bool value) => OnPropertyChanged(nameof(IsIdle));
    private void OnDataChanged() => OnPropertyChanged(nameof(HasUnsavedChanges));

    private void OnWarehouseDataSaved()
    {
        _ = DispatcherHelper.RunOnMainThreadAsync(() =>
        {
            OnPropertyChanged(nameof(LastUpdatedText));
            OnPropertyChanged(nameof(AutoRecognizeHint));
            RefreshSavedData();
        });
    }

    private void RefreshSavedData()
    {
        if (HasUnsavedChanges)
        {
            LoggerHelper.Warning("[仓库] 页面存在未保存修改，跳过外部保存后的自动刷新");
            return;
        }

        var data = ConfigurationManager.Current.GetValue(ConfigurationKeys.WarehouseData, new WarehouseData());
        _editor.LoadData(data);
        _pendingResourceHistory = [];
        _hasPendingChartChanges = false;

        foreach (var item in CoreResources)
        {
            var value = GetValue(data.CoreResources, item.Name);
            item.Count = Math.Clamp(value, 0, item.Maximum);
            _savedCore[item.Name] = value;
        }

        _savedOther.Clear();
        OtherItems.Clear();
        foreach (var pair in WarehouseScanDraftService.NormalizeOtherItems(data.OtherItems))
        {
            OtherItems.Add(new WarehouseOtherItemViewModel(pair.Key, pair.Value, OnDataChanged));
            _savedOther[pair.Key] = pair.Value;
        }

        RebuildCharts();
        OnPropertyChanged(nameof(HasOtherItems));
        OnPropertyChanged(nameof(NoOtherItems));
        OnPropertyChanged(nameof(HasChartHistory));
        OnPropertyChanged(nameof(NoChartHistory));
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private void LoadDraft()
    {
        var draft = WarehouseScanDraftService.Load(DraftPath);
        foreach (var item in CoreResources)
        {
            if (draft.CoreResources.TryGetValue(item.Name, out var value))
                item.Count = Math.Clamp(value, 0, item.Maximum);
        }

        OtherItems.Clear();
        foreach (var pair in WarehouseScanDraftService.NormalizeOtherItems(draft.OtherItems))
        {
            if (pair.Value <= 0)
                continue;
            OtherItems.Add(new WarehouseOtherItemViewModel(pair.Key, pair.Value, OnDataChanged));
        }

        _pendingResourceHistory = [.. draft.ResourceHistory.Select(CloneSnapshot)];
        OnPropertyChanged(nameof(HasOtherItems));
        OnPropertyChanged(nameof(NoOtherItems));
        OnPropertyChanged(nameof(HasChartHistory));
        OnPropertyChanged(nameof(NoChartHistory));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        ToastHelper.Success("仓库识别", "识别完成，结果已填入页面；点击“保存”后才会写入正式数据。");
    }
    private bool HasCoreChanged(WarehouseCoreResourceViewModel item) => item.Count != GetValue(_savedCore, item.Name);
    private bool HasOtherChanged(WarehouseOtherItemViewModel item) => item.Count != GetValue(_savedOther, item.Name);

    [RelayCommand]
    private void RefreshCharts()
    {
        RebuildCharts();
    }

    [RelayCommand]
    private void AddOtherItem()
    {
        OtherItems.Add(new WarehouseOtherItemViewModel(string.Empty, 0, OnDataChanged));
        OnPropertyChanged(nameof(HasOtherItems));
        OnPropertyChanged(nameof(NoOtherItems));
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    /// <summary>移动其他物品的显示顺序。</summary>
    public void MoveOtherItem(int sourceIndex, int targetIndex)
    {
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex >= OtherItems.Count || targetIndex >= OtherItems.Count || sourceIndex == targetIndex)
            return;

        OtherItems.Move(sourceIndex, targetIndex);
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    [RelayCommand]
    private async Task ClearChartHistory()
    {
        if (_editor.Data.ResourceHistory.Count == 0)
            return;

        var result = await SukiMessageBox.ShowDialog(new SukiMessageBoxHost
        {
            Content = "确定要清空所有核心资源变化记录吗？",
            ActionButtonsPreset = SukiMessageBoxButtons.YesNo,
            IconPreset = SukiMessageBoxIcons.Warning,
        }, new SukiMessageBoxOptions { Title = "清空图表数据" });
        if (!result.Equals(SukiMessageBoxResult.Yes))
            return;

        _editor.Data.ResourceHistory.Clear();
        _hasPendingChartChanges = true;
        RebuildCharts();
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private void RebuildCharts()
    {
        var comparisonScrollValue = ComparisonChart?.ScrollValue ?? 0;
        var chartScrollValues = Charts.ToDictionary(chart => chart.Name, chart => chart.ScrollValue, StringComparer.Ordinal);
        Charts.Clear();
        var now = DateTime.Now;
        var filteredHistory = WarehouseResourceHistoryFilter.FilterWithIndices(
            _editor.Data.ResourceHistory, SelectedChartRange, now);
        ChartZoomPercent = Math.Min(ChartZoomPercent, GetMaximumChartZoom());
        var baseBounds = GetChartTimeBounds(filteredHistory.Select(item => item.Snapshot).ToList(), SelectedChartRange, now);
        var markerPointLimit = GetMarkerPointLimit();
        ComparisonChart = new WarehouseComparisonChartViewModel(
            _comparisonSelection.SelectedResourceNames,
            filteredHistory,
            _editor.Data.ResourceHistory,
            SelectedChartRange,
            baseBounds.Start,
            baseBounds.End,
            ChartCanvasWidth,
            _chartDisplayWidth,
            Math.Min(comparisonScrollValue, Math.Max(0, ChartCanvasWidth - _chartDisplayWidth)),
            markerPointLimit,
            DeleteChartPoint);
        foreach (var definition in CoreDefinitions.Where(definition =>
                     !WarehouseComparisonResourceSelection.AvailableResourceNames.Contains(definition.Name, StringComparer.Ordinal)))
            Charts.Add(new WarehouseChartViewModel(definition.Name, filteredHistory, _editor.Data.ResourceHistory,
                SelectedChartRange, baseBounds.Start, baseBounds.End, ChartCanvasWidth, _chartDisplayWidth,
                Math.Min(chartScrollValues.GetValueOrDefault(definition.Name), Math.Max(0, ChartCanvasWidth - _chartDisplayWidth)),
                markerPointLimit, DeleteChartPoint));
        OnPropertyChanged(nameof(ComparisonChart));
        OnPropertyChanged(nameof(HasChartHistory));
        OnPropertyChanged(nameof(NoChartHistory));
    }

    private void ToggleComparisonResource(string resourceName)
    {
        if (!_comparisonSelection.Toggle(resourceName))
            return;

        foreach (var resource in ComparisonResources)
            resource.IsSelected = _comparisonSelection.SelectedResourceNames.Contains(resource.Name, StringComparer.Ordinal);

        RebuildCharts();
    }

    public void UpdateChartDisplayWidth(double width)
    {
        var normalizedWidth = Math.Max(1, width);
        if (Math.Abs(_chartDisplayWidth - normalizedWidth) < 1)
            return;

        _chartDisplayWidth = normalizedWidth;
        OnPropertyChanged(nameof(ChartCanvasWidth));
        RebuildCharts();
    }

    private int GetMarkerPointLimit() => Math.Max(30, (int)(_chartDisplayWidth / 20));

    [RelayCommand]
    private void SelectLast24Hours() => SetChartRange(WarehouseChartRange.Last24Hours);

    [RelayCommand]
    private void SelectLast7Days() => SetChartRange(WarehouseChartRange.Last7Days);

    [RelayCommand]
    private void SelectLast30Days() => SetChartRange(WarehouseChartRange.Last30Days);

    private void SetChartRange(WarehouseChartRange range)
    {
        SelectedChartRange = SelectedChartRange == range ? WarehouseChartRange.All : range;
        ChartZoomPercent = 100;
        OnPropertyChanged(nameof(IsLast24HoursSelected));
        OnPropertyChanged(nameof(IsLast7DaysSelected));
        OnPropertyChanged(nameof(IsLast30DaysSelected));
        OnPropertyChanged(nameof(Last24HoursButtonText));
        OnPropertyChanged(nameof(Last7DaysButtonText));
        OnPropertyChanged(nameof(Last30DaysButtonText));
        RebuildCharts();
    }

    [RelayCommand]
    private void ZoomChartIn()
    {
        var maximumZoom = GetMaximumChartZoom();
        ChartZoomPercent = Math.Min(maximumZoom, ChartZoomPercent + 25);
        RebuildCharts();
    }

    [RelayCommand]
    private void ZoomChartOut()
    {
        ChartZoomPercent = Math.Max(100, ChartZoomPercent - 25);
        RebuildCharts();
    }

    partial void OnChartZoomPercentChanged(int value)
    {
        OnPropertyChanged(nameof(ChartZoomText));
        OnPropertyChanged(nameof(ChartCanvasWidth));
        OnPropertyChanged(nameof(CanZoomChartIn));
        OnPropertyChanged(nameof(CanZoomChartOut));
    }

    private int GetMaximumChartZoom()
    {
        return 500;
    }

    private static (DateTime Start, DateTime End) GetChartTimeBounds(
        IReadOnlyList<WarehouseResourceSnapshot> history,
        WarehouseChartRange range,
        DateTime now)
    {
        if (range == WarehouseChartRange.All)
            return history.Count == 0
                ? (now, now)
                : (history.Min(snapshot => snapshot.RecordedAt), history.Max(snapshot => snapshot.RecordedAt));

        var duration = range switch
        {
            WarehouseChartRange.Last24Hours => TimeSpan.FromHours(24),
            WarehouseChartRange.Last7Days => TimeSpan.FromDays(7),
            WarehouseChartRange.Last30Days => TimeSpan.FromDays(30),
            _ => TimeSpan.Zero,
        };
        return (now - duration, now);
    }

    private static WarehouseResourceSnapshot CloneSnapshot(WarehouseResourceSnapshot snapshot) => new()
    {
        RecordedAt = snapshot.RecordedAt,
        Values = new Dictionary<string, int>(snapshot.Values, StringComparer.Ordinal),
    };

    private void DeleteChartPoint(string resourceName, int historyIndex)
    {
        if (historyIndex < 0 || historyIndex >= _editor.Data.ResourceHistory.Count)
            return;

        var snapshot = _editor.Data.ResourceHistory[historyIndex];
        if (!snapshot.Values.Remove(resourceName))
            return;

        if (snapshot.Values.Count == 0)
            _editor.Data.ResourceHistory.RemoveAt(historyIndex);

        _hasPendingChartChanges = true;
        RebuildCharts();
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private static int GetValue(System.Collections.Generic.Dictionary<string, int> values, string key) =>
        values.TryGetValue(key, out var value) ? value : 0;

    private static Bitmap? LoadIcon(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;
        var path = Path.Combine(AppPaths.ResourceDirectory, "base", "image", "resource-icon", fileName);
        return File.Exists(path) ? new Bitmap(path) : null;
    }
}

public sealed partial class WarehouseCoreResourceViewModel : ObservableObject
{
    private readonly Action _changed;
    public WarehouseCoreResourceViewModel(string name, string displayName, Bitmap? icon, int maximum, int count, Action changed)
    {
        Name = name; DisplayName = displayName; Icon = icon; Maximum = maximum; _count = count; _changed = changed;
    }
    public string Name { get; }
    public string DisplayName { get; }
    public Bitmap? Icon { get; }
    public int Maximum { get; }
    public bool HasIcon => Icon != null;
    public bool HasNoIcon => Icon == null;
    [ObservableProperty] private int _count;
    partial void OnCountChanged(int value) => _changed();
}

public sealed partial class WarehouseOtherItemViewModel : ObservableObject
{
    private readonly Action _changed;
    public WarehouseOtherItemViewModel(string name, int count, Action changed)
    {
        _name = name; _count = count; _changed = changed;
    }
    [ObservableProperty] private string _name;
    [ObservableProperty] private int _count;
    public bool IsVisible => Count > 0 || string.IsNullOrWhiteSpace(Name);
    partial void OnNameChanged(string value) => _changed();
    partial void OnCountChanged(int value)
    {
        OnPropertyChanged(nameof(IsVisible));
        _changed();
    }
}

public sealed partial class WarehouseComparisonResourceOption : ObservableObject
{
    public WarehouseComparisonResourceOption(string name, bool isSelected, Action<string> toggle)
    {
        Name = name;
        IsSelected = isSelected;
        ToggleCommand = new RelayCommand(() => toggle(Name));
    }

    public string Name { get; }
    public string SelectionText => IsSelected ? $"✓ {Name}" : Name;
    public ICommand ToggleCommand { get; }
    [ObservableProperty] private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => OnPropertyChanged(nameof(SelectionText));
}

public sealed partial class WarehouseComparisonChartViewModel : ObservableObject
{
    public const double ChartWidth = 650;
    public const double ChartHeight = 230;
    private readonly WarehouseComparisonSelection _selection = new();
    private DateTime _start;
    private DateTime _end;
    [ObservableProperty] private double _scrollValue;

    public WarehouseComparisonChartViewModel(
        IEnumerable<string> resourceNames,
        IEnumerable<(WarehouseResourceSnapshot Snapshot, int Index)> history,
        IReadOnlyList<WarehouseResourceSnapshot> fullHistory,
        WarehouseChartRange range,
        DateTime start,
        DateTime end,
        double canvasWidth,
        double viewportWidth,
        double scrollOffset,
        int markerPointLimit,
        Action<string, int> deletePoint)
    {
        _start = start;
        _end = end;
        CanvasWidth = canvasWidth;
        ViewportWidth = viewportWidth;
        _scrollValue = Math.Clamp(scrollOffset, 0, Math.Max(0, canvasWidth - viewportWidth));
        VisualScale = Math.Min(1, viewportWidth / ChartWidth);
        ViewportHeight = ChartHeight * VisualScale;
        LegendMargin = new Thickness(8 * VisualScale, 4 * VisualScale, 0, 0);
        LegendTransform = new ScaleTransform(VisualScale, VisualScale);
        AxisLabelTop = 208 * VisualScale;
        AxisLabelFontSize = 10 * VisualScale;
        PathStrokeThickness = 2 * VisualScale;
        HorizontalGridStartPoint45 = new Point(0, 45 * VisualScale);
        HorizontalGridStartPoint95 = new Point(0, 95 * VisualScale);
        HorizontalGridStartPoint150 = new Point(0, 150 * VisualScale);
        HorizontalGridEndPoint45 = new Point(viewportWidth, 45 * VisualScale);
        HorizontalGridEndPoint95 = new Point(viewportWidth, 95 * VisualScale);
        HorizontalGridEndPoint150 = new Point(viewportWidth, 150 * VisualScale);
        VerticalGridStartPoint162 = new Point(viewportWidth * 0.25, 0);
        VerticalGridStartPoint325 = new Point(viewportWidth * 0.5, 0);
        VerticalGridStartPoint487 = new Point(viewportWidth * 0.75, 0);
        VerticalGridEndPoint162 = new Point(viewportWidth * 0.25, 205 * VisualScale);
        VerticalGridEndPoint325 = new Point(viewportWidth * 0.5, 205 * VisualScale);
        VerticalGridEndPoint487 = new Point(viewportWidth * 0.75, 205 * VisualScale);
        BaselineEndPoint = new Point(viewportWidth, 205 * VisualScale);
        BaselineStartPoint = new Point(0, 205 * VisualScale);
        var filteredHistory = history.ToList();
        foreach (var label in WarehouseChartTimeAxis.BuildVisibleLabels(start, end, canvasWidth, viewportWidth, ScrollValue, AxisLabelTop, AxisLabelFontSize))
            AxisLabels.Add(label);

        var values = resourceNames
            .SelectMany(name => filteredHistory
                .Where(item => item.Snapshot.Values.ContainsKey(name))
                .Select(item => item.Snapshot.Values[name]))
            .ToList();
        var minimum = values.Count == 0 ? 0 : values.Min();
        var maximum = values.Count == 0 ? 1 : values.Max();
        var colors = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["木炭"] = "#667085",
            ["玉钢"] = "#55CFA3",
            ["冷却材"] = "#6CBFE8",
            ["砥石"] = "#9B8AF5",
        };

        foreach (var name in resourceNames)
        {
            Series.Add(new WarehouseComparisonSeriesViewModel(
                name,
                colors[name],
                filteredHistory,
                fullHistory,
                start,
                end,
                minimum,
                maximum,
                canvasWidth,
                viewportWidth,
                ScrollValue,
                markerPointLimit,
                VisualScale,
                deletePoint,
                SelectPoint));
        }
    }

    public ObservableCollection<WarehouseComparisonSeriesViewModel> Series { get; } = [];
    public ObservableCollection<WarehouseChartTimeAxisLabel> AxisLabels { get; } = [];
    public double CanvasWidth { get; }
    public double ViewportWidth { get; }
    public double ScrollMaximum => Math.Max(0, CanvasWidth - ViewportWidth);
    public double ScrollViewportSize => ViewportWidth;
    public bool HasScrollBar => ScrollMaximum > 0;
    public double VisualScale { get; }
    public double ViewportHeight { get; }
    public Thickness LegendMargin { get; }
    public ScaleTransform LegendTransform { get; }
    public double AxisLabelTop { get; }
    public double AxisLabelFontSize { get; }
    public double PathStrokeThickness { get; }
    public Point HorizontalGridStartPoint45 { get; }
    public Point HorizontalGridStartPoint95 { get; }
    public Point HorizontalGridStartPoint150 { get; }
    public Point HorizontalGridEndPoint45 { get; }
    public Point HorizontalGridEndPoint95 { get; }
    public Point HorizontalGridEndPoint150 { get; }
    public Point VerticalGridStartPoint162 { get; }
    public Point VerticalGridStartPoint325 { get; }
    public Point VerticalGridStartPoint487 { get; }
    public Point VerticalGridEndPoint162 { get; }
    public Point VerticalGridEndPoint325 { get; }
    public Point VerticalGridEndPoint487 { get; }
    public Point BaselineEndPoint { get; }
    public Point BaselineStartPoint { get; }

    partial void OnScrollValueChanged(double value)
    {
        var clampedValue = Math.Clamp(value, 0, ScrollMaximum);
        if (Math.Abs(value - clampedValue) > 0.01)
        {
            _scrollValue = clampedValue;
            OnPropertyChanged(nameof(ScrollValue));
        }

        foreach (var series in Series)
            series.UpdateHorizontalOffset(clampedValue);
        AxisLabels.Clear();
        foreach (var label in WarehouseChartTimeAxis.BuildVisibleLabels(_start, _end, CanvasWidth, ViewportWidth, clampedValue, AxisLabelTop, AxisLabelFontSize))
            AxisLabels.Add(label);
        OnPropertyChanged(nameof(ScrollMaximum));
    }
    public bool HasHistory => Series.Any(series => series.HasHistory);
    [ObservableProperty] private bool _hasCompletedSelection;
    [ObservableProperty] private string _selectionSummaryText = string.Empty;

    private void SelectPoint(WarehouseChartPointViewModel point)
    {
        var completed = _selection.Select(point.RecordedAt);
        foreach (var series in Series)
        {
            foreach (var seriesPoint in series.Points)
            {
                seriesPoint.SetSelected(
                    seriesPoint.RecordedAt == _selection.Start
                    || seriesPoint.RecordedAt == _selection.End);
            }
        }

        HasCompletedSelection = completed;
        SelectionSummaryText = completed ? BuildSelectionSummary() : string.Empty;
        OnPropertyChanged(nameof(TimeSummaryText));
    }

    private string BuildSelectionSummary()
    {
        var start = _selection.Start!.Value;
        var end = _selection.End!.Value;
        SelectionRows.Clear();
        var summaries = Series.Select(series =>
        {
            var startPoint = series.Points.FirstOrDefault(point => point.RecordedAt == start);
            var endPoint = series.Points.FirstOrDefault(point => point.RecordedAt == end);
            if (startPoint == null || endPoint == null)
            {
                SelectionRows.Add(new WarehouseComparisonSelectionRowViewModel(series.Name, "数据不完整"));
                return $"{series.Name}：数据不完整";
            }

            var change = endPoint.Value - startPoint.Value;
            SelectionRows.Add(new WarehouseComparisonSelectionRowViewModel(
                series.Name,
                $"{startPoint.Value:N0} → {endPoint.Value:N0}（{change:+#,##0;-#,##0;0}）"));
            return $"{series.Name} {startPoint.Value:N0} → {endPoint.Value:N0}（{change:+#,##0;-#,##0;0}）";
        });
        return $"起点：{start:yyyy-MM-dd HH:mm:ss} · 终点：{end:yyyy-MM-dd HH:mm:ss}\n{string.Join("　", summaries)}";
    }

    public ObservableCollection<WarehouseComparisonSelectionRowViewModel> SelectionRows { get; } = [];
    public string TimeSummaryText => _selection.Duration is { } duration
        ? $"起始：{_selection.Start:yyyy-MM-dd HH:mm:ss} · 终止：{_selection.End:yyyy-MM-dd HH:mm:ss}\n相隔：{FormatDuration(duration)}"
        : string.Empty;

    private static string FormatDuration(TimeSpan duration)
    {
        var totalHours = (int)duration.TotalHours;
        return totalHours > 0
            ? $"{totalHours}小时{duration.Minutes}分钟"
            : $"{duration.Minutes}分钟";
    }
}

public sealed class WarehouseComparisonSelectionRowViewModel
{
    public WarehouseComparisonSelectionRowViewModel(string name, string summary)
    {
        Name = name;
        Summary = summary;
    }

    public string Name { get; }
    public string Summary { get; }
}

public sealed partial class WarehouseComparisonSeriesViewModel : ObservableObject
{
    public WarehouseComparisonSeriesViewModel(
        string name,
        string color,
        IReadOnlyList<(WarehouseResourceSnapshot Snapshot, int Index)> history,
        IReadOnlyList<WarehouseResourceSnapshot> fullHistory,
        DateTime start,
        DateTime end,
        int minimum,
        int maximum,
        double canvasWidth,
        double viewportWidth,
        double scrollOffset,
        int markerPointLimit,
        double visualScale,
        Action<string, int> deletePoint,
        Action<WarehouseChartPointViewModel> selectPoint)
    {
        Name = name;
        Color = color;
        CanvasWidth = canvasWidth;
        ViewportWidth = viewportWidth;
        VisualScale = visualScale;
        CanvasHeight = WarehouseComparisonChartViewModel.ChartHeight * visualScale;
        PathStrokeThickness = 2 * visualScale;
        var allSnapshots = history.Where(item => item.Snapshot.Values.ContainsKey(name)).ToList();
        HasHistory = allSnapshots.Count > 0;
        var allValues = allSnapshots.Select(item => item.Snapshot.Values[name]).ToList();
        PointCount = allValues.Count;
        if (allValues.Count > 0)
        {
            LatestValue = allValues[^1];
            MinimumValue = allValues.Min();
            MaximumValue = allValues.Max();
        }
        var snapshots = allSnapshots.Where(item => item.Snapshot.RecordedAt >= start && item.Snapshot.RecordedAt <= end).ToList();
        var values = snapshots.Select(item => item.Snapshot.Values[name]).ToList();

        var span = Math.Max(1d, maximum - minimum);
        var height = 185d;
        var visiblePointCount = WarehouseChartGeometry.CountVisiblePoints(
            snapshots.Select(item => item.Snapshot.RecordedAt), start, end, canvasWidth, viewportWidth, scrollOffset);
        var showMarkers = visiblePointCount <= markerPointLimit;
        for (var i = 0; i < values.Count; i++)
        {
            var x = WarehouseChartGeometry.MapX(snapshots[i].Snapshot.RecordedAt, start, end, canvasWidth);
            var y = (10 + height - (values[i] - minimum) / span * height) * visualScale;
            var change = FindChange(fullHistory, snapshots[i].Index, name, values[i]);
            Points.Add(new WarehouseChartPointViewModel(
                x, y, values[i], change, snapshots[i].Snapshot.RecordedAt, snapshots[i].Index,
                name, deletePoint, selectPoint, color, isMarkerVisible: showMarkers,
                visualScale: visualScale, horizontalOffset: scrollOffset));
        }

        if (Points.Count < 2)
            return;

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(Points[0].X, Points[0].Y), false);
            foreach (var point in Points.Skip(1))
                context.LineTo(new Point(point.X, point.Y));
        }
        LineGeometry = geometry;
    }

    public string Name { get; }
    public string Color { get; }
    public double CanvasWidth { get; }
    public double ViewportWidth { get; }
    public double VisualScale { get; }
    public double CanvasHeight { get; }
    public double PathStrokeThickness { get; }
    public int PointCount { get; }
    public bool HasHistory { get; }
    public int MinimumValue { get; }
    public int MaximumValue { get; }
    public int LatestValue { get; }
    public string SummaryText => PointCount == 0
        ? "暂无识别记录"
        : $"当前 {LatestValue:N0} · 范围 {MinimumValue:N0}–{MaximumValue:N0}";
    public Geometry? LineGeometry { get; private set; }
    public ObservableCollection<WarehouseChartPointViewModel> Points { get; } = [];

    public void UpdateHorizontalOffset(double offset)
    {
        foreach (var point in Points)
            point.SetHorizontalOffset(offset);

        LineGeometry = BuildLineGeometry(Points);
        OnPropertyChanged(nameof(LineGeometry));
    }

    private static Geometry? BuildLineGeometry(IReadOnlyList<WarehouseChartPointViewModel> points)
    {
        if (points.Count < 2)
            return null;

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(points[0].X, points[0].Y), false);
            foreach (var point in points.Skip(1))
                context.LineTo(new Point(point.X, point.Y));
        }
        return geometry;
    }

    private static int? FindChange(IReadOnlyList<WarehouseResourceSnapshot> history, int index, string name, int value)
    {
        for (var previousIndex = index - 1; previousIndex >= 0; previousIndex--)
        {
            if (history[previousIndex].Values.TryGetValue(name, out var previousValue))
                return value - previousValue;
        }

        return null;
    }
}

public sealed partial class WarehouseChartViewModel : ObservableObject
{
    public const double ChartWidth = 650;
    public const double ChartHeight = 170;
    private const double AxisY = 145;
    private readonly WarehouseChartRangeSelection _rangeSelection = new();
    private DateTime _start;
    private DateTime _end;
    [ObservableProperty] private double _scrollValue;
    private WarehouseChartPointViewModel? _firstSelectedPoint;
    private WarehouseChartPointViewModel? _secondSelectedPoint;
    public ObservableCollection<WarehouseChartPointViewModel> Points { get; } = [];
    public ObservableCollection<WarehouseChartTimeAxisLabel> AxisLabels { get; } = [];
    [ObservableProperty] private bool _hasCompletedSelection;
    [ObservableProperty] private string _selectionSummaryText = string.Empty;

    public WarehouseChartViewModel(string name, IEnumerable<(WarehouseResourceSnapshot Snapshot, int Index)> history,
        IReadOnlyList<WarehouseResourceSnapshot> fullHistory,
        WarehouseChartRange range, DateTime start, DateTime end, double canvasWidth, double viewportWidth,
        double scrollOffset, int markerPointLimit, Action<string, int> deletePoint)
    {
        _start = start;
        _end = end;
        Name = name;
        CanvasWidth = canvasWidth;
        ViewportWidth = viewportWidth;
        _scrollValue = Math.Clamp(scrollOffset, 0, Math.Max(0, canvasWidth - viewportWidth));
        VisualScale = Math.Min(1, viewportWidth / ChartWidth);
        ViewportHeight = ChartHeight * VisualScale;
        AxisLabelTop = 148 * VisualScale;
        AxisLabelFontSize = 10 * VisualScale;
        PathStrokeThickness = 2 * VisualScale;
        HorizontalGridStartPoint30 = new Point(0, 30 * VisualScale);
        HorizontalGridStartPoint68 = new Point(0, 68 * VisualScale);
        HorizontalGridStartPoint106 = new Point(0, 106 * VisualScale);
        HorizontalGridEndPoint30 = new Point(viewportWidth, 30 * VisualScale);
        HorizontalGridEndPoint68 = new Point(viewportWidth, 68 * VisualScale);
        HorizontalGridEndPoint106 = new Point(viewportWidth, 106 * VisualScale);
        VerticalGridStartPoint162 = new Point(viewportWidth * 0.25, 0);
        VerticalGridStartPoint325 = new Point(viewportWidth * 0.5, 0);
        VerticalGridStartPoint487 = new Point(viewportWidth * 0.75, 0);
        VerticalGridEndPoint162 = new Point(viewportWidth * 0.25, AxisY * VisualScale);
        VerticalGridEndPoint325 = new Point(viewportWidth * 0.5, AxisY * VisualScale);
        VerticalGridEndPoint487 = new Point(viewportWidth * 0.75, AxisY * VisualScale);
        BaselineEndPoint = new Point(viewportWidth, AxisY * VisualScale);
        BaselineStartPoint = new Point(0, AxisY * VisualScale);
        var allSnapshots = history
            .Where(item => item.Snapshot.Values.ContainsKey(name))
            .ToList();
        HasHistory = allSnapshots.Count > 0;
        var allValues = allSnapshots.Select(item => item.Snapshot.Values[name]).ToList();
        PointCount = allValues.Count;
        if (allValues.Count > 0)
        {
            MinimumValue = allValues.Min();
            MaximumValue = allValues.Max();
            LatestValue = allValues[^1];
        }
        var snapshots = allSnapshots.Where(item => item.Snapshot.RecordedAt >= start && item.Snapshot.RecordedAt <= end).ToList();
        var values = snapshots.Select(item => item.Snapshot.Values[name]).ToList();
        foreach (var label in WarehouseChartTimeAxis.BuildVisibleLabels(start, end, canvasWidth, viewportWidth, ScrollValue, AxisLabelTop, AxisLabelFontSize))
            AxisLabels.Add(label);
        BuildPoints(name, snapshots, values, start, end, MinimumValue, MaximumValue, canvasWidth, viewportWidth,
            ScrollValue, markerPointLimit, fullHistory, deletePoint);
    }
    public string Name { get; }
    public int PointCount { get; }
    public bool HasHistory { get; }
    public string EmptyText => PointCount == 0 ? "暂无识别记录" : $"已有 {PointCount} 个记录点";
    public int MinimumValue { get; }
    public int MaximumValue { get; }
    public int LatestValue { get; }
    public string SummaryText => PointCount == 0
        ? "暂无识别记录"
        : $"当前 {LatestValue:N0} · 范围 {MinimumValue:N0}–{MaximumValue:N0}";
    public Geometry? LineGeometry { get; private set; }
    public double CanvasWidth { get; }
    public double ViewportWidth { get; }
    public double ScrollMaximum => Math.Max(0, CanvasWidth - ViewportWidth);
    public double ScrollViewportSize => ViewportWidth;
    public bool HasScrollBar => ScrollMaximum > 0;
    public double VisualScale { get; }
    public double ViewportHeight { get; }
    public double AxisLabelTop { get; }
    public double AxisLabelFontSize { get; }
    public double PathStrokeThickness { get; }
    public Point HorizontalGridStartPoint30 { get; }
    public Point HorizontalGridStartPoint68 { get; }
    public Point HorizontalGridStartPoint106 { get; }
    public Point HorizontalGridEndPoint30 { get; }
    public Point HorizontalGridEndPoint68 { get; }
    public Point HorizontalGridEndPoint106 { get; }
    public Point VerticalGridStartPoint162 { get; }
    public Point VerticalGridStartPoint325 { get; }
    public Point VerticalGridStartPoint487 { get; }
    public Point VerticalGridEndPoint162 { get; }
    public Point VerticalGridEndPoint325 { get; }
    public Point VerticalGridEndPoint487 { get; }
    public Point BaselineEndPoint { get; }
    public Point BaselineStartPoint { get; }

    partial void OnScrollValueChanged(double value)
    {
        var clampedValue = Math.Clamp(value, 0, ScrollMaximum);
        if (Math.Abs(value - clampedValue) > 0.01)
        {
            _scrollValue = clampedValue;
            OnPropertyChanged(nameof(ScrollValue));
        }

        foreach (var point in Points)
            point.SetHorizontalOffset(clampedValue);
        LineGeometry = BuildLineGeometry(Points);
        OnPropertyChanged(nameof(LineGeometry));
        AxisLabels.Clear();
        foreach (var label in WarehouseChartTimeAxis.BuildVisibleLabels(_start, _end, CanvasWidth, ViewportWidth, clampedValue, AxisLabelTop, AxisLabelFontSize))
            AxisLabels.Add(label);
        OnPropertyChanged(nameof(ScrollMaximum));
    }

    private void BuildPoints(string name, IReadOnlyList<(WarehouseResourceSnapshot Snapshot, int Index)> snapshots,
        IReadOnlyList<int> values, DateTime start, DateTime end, int minimum, int maximum, double canvasWidth,
        double viewportWidth, double scrollOffset, int markerPointLimit,
        IReadOnlyList<WarehouseResourceSnapshot> fullHistory, Action<string, int> deletePoint)
    {
        if (values.Count == 0)
            return;

        var span = Math.Max(1d, maximum - minimum);
        var height = AxisY - 20;
        var visiblePointCount = WarehouseChartGeometry.CountVisiblePoints(
            snapshots.Select(item => item.Snapshot.RecordedAt), start, end, canvasWidth, viewportWidth, scrollOffset);
        var showMarkers = visiblePointCount <= markerPointLimit;
        for (var i = 0; i < values.Count; i++)
        {
            var x = WarehouseChartGeometry.MapX(snapshots[i].Snapshot.RecordedAt, start, end, canvasWidth);
            var y = (10 + height - (values[i] - minimum) / span * height) * VisualScale;
            var change = FindChange(fullHistory, snapshots[i].Index, name, values[i]);
            Points.Add(new WarehouseChartPointViewModel(x, y, values[i], change, snapshots[i].Snapshot.RecordedAt,
                snapshots[i].Index, name, deletePoint, SelectPoint, isMarkerVisible: showMarkers,
                horizontalScale: 1, visualScale: VisualScale, horizontalOffset: scrollOffset));
        }

        if (Points.Count < 2)
            return;

        LineGeometry = BuildLineGeometry(Points);
    }

    private static Geometry? BuildLineGeometry(IReadOnlyList<WarehouseChartPointViewModel> points)
    {
        if (points.Count < 2)
            return null;

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(points[0].X, points[0].Y), false);
            foreach (var point in points.Skip(1))
                context.LineTo(new Point(point.X, point.Y));
        }
        return geometry;
    }

    private void SelectPoint(WarehouseChartPointViewModel point)
    {
        var startsNewSelection = _rangeSelection.End != null;
        if (startsNewSelection)
            ClearSelectedPoints();

        var result = _rangeSelection.Select(point.RecordedAt, point.Value);
        if (startsNewSelection || _firstSelectedPoint == null)
        {
            _firstSelectedPoint = point;
        }
        else
        {
            _secondSelectedPoint = point;
        }

        point.IsSelected = true;
        HasCompletedSelection = result != null;
        SelectionSummaryText = result?.SummaryText ?? string.Empty;
    }

    private void ClearSelectedPoints()
    {
        if (_firstSelectedPoint != null)
            _firstSelectedPoint.IsSelected = false;
        if (_secondSelectedPoint != null)
            _secondSelectedPoint.IsSelected = false;

        _firstSelectedPoint = null;
        _secondSelectedPoint = null;
    }

    private static int? FindChange(IReadOnlyList<WarehouseResourceSnapshot> history, int index, string name, int value)
    {
        for (var previousIndex = index - 1; previousIndex >= 0; previousIndex--)
        {
            if (history[previousIndex].Values.TryGetValue(name, out var previousValue))
                return value - previousValue;
        }

        return null;
    }
}

internal static class WarehouseChartGeometry
{
    public static int CountVisiblePoints(
        IEnumerable<DateTime> timestamps,
        DateTime start,
        DateTime end,
        double canvasWidth,
        double viewportWidth,
        double scrollOffset)
    {
        return timestamps.Count(timestamp =>
        {
            var x = MapX(timestamp, start, end, canvasWidth);
            return x >= scrollOffset && x <= scrollOffset + viewportWidth;
        });
    }

    public static double MapX(DateTime recordedAt, DateTime start, DateTime end, double canvasWidth)
    {
        const double horizontalPadding = 18;
        var width = Math.Max(1, canvasWidth - horizontalPadding * 2);
        var elapsedTicks = (end - start).Ticks;
        return elapsedTicks > 0
            ? horizontalPadding + width * (recordedAt - start).Ticks / elapsedTicks
            : canvasWidth / 2;
    }

}

public sealed partial class WarehouseChartPointViewModel : ObservableObject
{
    public WarehouseChartPointViewModel(double x, double y, int value, int? change, DateTime recordedAt,
        int historyIndex, string resourceName, Action<string, int> deletePoint,
        Action<WarehouseChartPointViewModel> selectPoint, string color = "#5B8FF9", bool isMarkerVisible = true,
        double horizontalScale = 1, double visualScale = 1, double horizontalOffset = 0)
    {
        _worldX = x;
        _horizontalOffset = horizontalOffset;
        X = x - horizontalOffset;
        Y = y;
        Value = value;
        Change = change;
        RecordedAt = recordedAt;
        DeleteCommand = new RelayCommand(() => deletePoint(resourceName, historyIndex));
        SelectCommand = new RelayCommand(() => selectPoint(this));
        PointColor = color;
        IsMarkerVisible = isMarkerVisible;
        MarkerSize = 12 * visualScale;
        MarkerStrokeThickness = 2 * visualScale;
        MarkerWidth = horizontalScale > 0 ? MarkerSize / horizontalScale : MarkerSize;
    }

    private readonly double _worldX;
    private double _horizontalOffset;
    public double X { get; private set; }
    public double Y { get; }
    public double MarkerWidth { get; }
    public double MarkerSize { get; }
    public double MarkerStrokeThickness { get; }
    public double Left => X - MarkerWidth / 2;
    public double Top => Y - MarkerSize / 2;
    public int Value { get; }
    public int? Change { get; }
    public DateTime RecordedAt { get; }
    public string RecordedAtText => RecordedAt.ToString("yyyy-MM-dd HH:mm:ss");
    public string CurrentText => $"当前：{Value:N0}";
    public string ChangeValueText => Change.HasValue
        ? $"{Change.Value:+#,##0;-#,##0;0}"
        : "—";
    public IBrush ChangeBrush => Change switch
    {
        > 0 => new SolidColorBrush(Color.Parse("#2E9B62")),
        < 0 => new SolidColorBrush(Color.Parse("#D05A5A")),
        _ => new SolidColorBrush(Color.Parse("#7B8798")),
    };
    public string TooltipText => WarehouseChartTooltipFormatter.Format(RecordedAt, Value, Change);
    public ICommand DeleteCommand { get; }
    public ICommand SelectCommand { get; }
    public string PointColor { get; }
    [ObservableProperty] private bool _isSelected;
    public bool IsMarkerVisible { get; }
    public double MarkerOpacity => IsSelected || IsMarkerVisible ? 1 : 0;
    public IBrush PointBrush => IsSelected
        ? new SolidColorBrush(Color.Parse("#F0A93A"))
        : new SolidColorBrush(Color.Parse(PointColor));

    public void SetSelected(bool value) => IsSelected = value;

    public void SetHorizontalOffset(double offset)
    {
        if (Math.Abs(_horizontalOffset - offset) < 0.01)
            return;

        _horizontalOffset = offset;
        X = _worldX - offset;
        OnPropertyChanged(nameof(X));
        OnPropertyChanged(nameof(Left));
    }

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(PointBrush));
        OnPropertyChanged(nameof(MarkerOpacity));
    }
}
