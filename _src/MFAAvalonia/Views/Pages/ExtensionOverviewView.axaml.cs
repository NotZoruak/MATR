using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MATR.ExtensionSdk;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Services;
using MFAAvalonia.Views.UserControls;
using Newtonsoft.Json;

namespace MFAAvalonia.Views.Pages;

/// <summary>独立扩展的导航与共享数据宿主，页面内容完全由包提供。</summary>
public partial class ExtensionOverviewView : UserControl
{
    private const string HeldSwordsWorkspaceId = "matr.held-swords";
    private const string NavigationOrderKey = "Extension.NavigationOrder";
    private const string DisabledPackagesKey = "Extension.DisabledPackages";
    private const double DragStartThreshold = 6;
    private bool _navigationExpanded = true;
    private GameOverviewLogParser.Snapshot? _snapshot;
    private DateTimeOffset? _snapshotCollectedAt;
    private bool _reading;
    private bool _syncingWarehouse;
    private DateTime _nextWarehouseSync = DateTime.MinValue;
    private bool _warehouseAutomaticWasEnabled;
    private string _lastStaleWarning = "";
    private bool _automaticWasEnabled;
    private DateTime _nextRead = DateTime.MinValue;
    private string _displayedDevice = "";
    private string _selected = "";
    private ExtensionState _state = new(null, "等待首次同步");
    private ExtensionState _heldSwordsState = new(null, "等待手动同步");
    private readonly DispatcherTimer _readTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Dictionary<string, LoadedExtension> _loaded = new();
    private readonly Dictionary<string, Control> _views = new();
    private readonly Dictionary<string, RadioButton> _navigationButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MenuItem> _navigationMenuItems = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<TextBlock> _navigationLabels = [];
    private readonly List<ExtensionPackage> _packages;
    private readonly HashSet<string> _disabledPackages;
    private string? _dragCandidate;
    private Avalonia.Point _dragStart;
    private bool _dragging;
    private bool _navigationDragMoved;
    private bool _suppressNavigationClick;
    private int _dragOriginalIndex = -1;
    private int _dragTargetIndex = -1;
    private DragAdorner? _navigationAdorner;

    private readonly string _configDirectory;

    public ExtensionOverviewView() : this(ExtensionContentService.Content, AppPaths.ConfigDirectory, true) { }

    public ExtensionOverviewView(ExtensionPackageCatalog catalog, string configDirectory, bool startServices = false)
    {
        _configDirectory = configDirectory;
        _packages = OrderPackages(catalog.Packages);
        _disabledPackages = ReadIdSet(DisabledPackagesKey);
        InitializeComponent();
        foreach (var package in _packages)
        {
            var label = new TextBlock { Text = package.Manifest.Name, VerticalAlignment = VerticalAlignment.Center };
            _navigationLabels.Add(label);
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            var icon = Enum.TryParse<FluentIcons.Common.Icon>(package.Manifest.Icon, out var parsed) ? parsed : FluentIcons.Common.Icon.Apps;
            content.Children.Add(new FluentIcons.Avalonia.Fluent.FluentIcon { Icon = icon, IconSize = FluentIcons.Common.IconSize.Size20 });
            content.Children.Add(label);
            var button = new RadioButton { Content = content, GroupName = "ExtensionNavigation" };
            button.Classes.Add("extension-navigation");
            _navigationButtons[package.Manifest.Id] = button;
            var menuItem = new MenuItem();
            menuItem.Click += (_, _) => TogglePackage(package);
            _navigationMenuItems[package.Manifest.Id] = menuItem;
            button.ContextMenu = new ContextMenu { Items = { menuItem } };
            UpdateNavigationButton(package);
            Avalonia.Automation.AutomationProperties.SetName(button, package.Manifest.Name);
            button.Click += (_, _) =>
            {
                if (_suppressNavigationClick)
                {
                    _suppressNavigationClick = false;
                    return;
                }
                SelectWorkspace(package);
            };
            WorkspaceNavigation.Children.Add(button);
        }
        WorkspaceNavigation.AddHandler(InputElement.PointerPressedEvent, NavigationPointerPressed, RoutingStrategies.Tunnel, true);
        WorkspaceNavigation.AddHandler(InputElement.PointerMovedEvent, NavigationPointerMoved, RoutingStrategies.Tunnel, true);
        WorkspaceNavigation.AddHandler(InputElement.PointerReleasedEvent, NavigationPointerReleased, RoutingStrategies.Tunnel, true);
        WorkspaceNavigation.AddHandler(InputElement.PointerCaptureLostEvent, NavigationPointerCaptureLost, RoutingStrategies.Bubble, true);
        var firstWorkspace = _packages.FirstOrDefault(IsPackageEnabled) ?? _packages.FirstOrDefault();
        if (firstWorkspace != null && _navigationButtons.TryGetValue(firstWorkspace.Manifest.Id, out var first))
        {
            first.IsChecked = true;
        }
        WorkspaceContent.AttachedToVisualTree += (_, _) =>
        {
            if (_selected.Length == 0 && firstWorkspace != null)
                SelectWorkspace(firstWorkspace);
            else if (firstWorkspace == null && WorkspaceContent.Children.Count == 0)
                ShowNoEnabledPackages();
        };
        if (startServices && _packages.Count > 0)
        {
            LoadSavedSnapshot();
            _readTimer.Tick += async (_, _) => await PollAsync();
            _readTimer.Start();
        }
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IControlledApplicationLifetime lifetime)
            lifetime.Exit += (_, _) => ReleaseWorkspaces();
    }

    private void SelectWorkspace(ExtensionPackage package)
    {
        _selected = package.Manifest.Id;
        if (!_views.TryGetValue(_selected, out var view))
        {
            try
            {
                var loaded = ExtensionPackageLoader.Load(package, Path.Combine(_configDirectory, "honmaru", "extension", "cache"));
                _loaded[_selected] = loaded;
                var dataDirectory = Path.Combine(_configDirectory, "honmaru", "extension", "workspaces", _selected);
                Directory.CreateDirectory(dataDirectory);
                view = loaded.Workspace.CreateView(new WorkspaceContext(dataDirectory, loaded.Directory, () => SyncWarehouseAsync(false)))
                    ?? throw new InvalidDataException("扩展没有返回页面。");
                loaded.Workspace.Update(_selected == HeldSwordsWorkspaceId ? _heldSwordsState : _state);
                _views[_selected] = view;
            }
            catch (Exception exception)
            {
                FailWorkspace(_selected, exception);
                view = _views[_selected];
            }
        }
        try { MountWorkspace(view); }
        catch (Exception exception)
        {
            // 挂载页面也会执行扩展代码，必须纳入同一故障边界。
            FailWorkspace(_selected, exception);
        }
    }

    private void MountWorkspace(Control view)
    {
        // 使用已挂载面板直接承载页面，避免内容模板把扩展挂载推迟到捕获范围之外。
        WorkspaceContent.Children.Clear();
        WorkspaceContent.Children.Add(view);
    }

    private void FailWorkspace(string id, Exception exception)
    {
        LoggerHelper.Warning("扩展工作区失败：" + id + "，类型：" + exception.GetType().Name);
        if (exception is MissingMethodException)
            LoggerHelper.Warning("扩展缺少运行时方法：" + exception.Message);
        if (_loaded.Remove(id, out var loaded))
            try { loaded.Dispose(); } catch (Exception disposeError) { LoggerHelper.Warning("释放扩展失败：" + disposeError.GetType().Name); }
        var view = new Border { Padding = new Avalonia.Thickness(20), Child = new TextBlock { Text = "此扩展未能加载或运行，请检查扩展包与 MATR 的接口版本。\n扩展：" + id + "\n错误类型：" + exception.GetType().Name, TextWrapping = TextWrapping.Wrap } };
        _views[id] = view;
        if (_selected == id)
            try { MountWorkspace(view); }
            catch (Exception detachError) { LoggerHelper.Warning("替换扩展错误页面失败：" + detachError.GetType().Name); }
    }

    private void PublishState(string status, bool refreshHeldSwords = true, bool clearHeldSwords = false)
    {
        _state = new ExtensionState(_snapshot == null ? null : JsonConvert.SerializeObject(_snapshot), status);
        if (clearHeldSwords)
            _heldSwordsState = new ExtensionState(null, "等待手动同步");
        else if (refreshHeldSwords)
            _heldSwordsState = _state;

        foreach (var entry in _loaded.ToArray())
        {
            if (_disabledPackages.Contains(entry.Key)) continue;
            var isHeldSwords = entry.Key == HeldSwordsWorkspaceId;
            if (isHeldSwords && !refreshHeldSwords && !clearHeldSwords) continue;
            try { entry.Value.Workspace.Update(isHeldSwords ? _heldSwordsState : _state); }
            catch (Exception exception) { FailWorkspace(entry.Key, exception); }
        }
    }

    private void ReleaseWorkspaces()
    {
        _readTimer.Stop();
        foreach (var entry in _loaded.Values)
            try { entry.Dispose(); } catch (Exception exception) { LoggerHelper.Warning("释放扩展失败：" + exception.GetType().Name); }
        _loaded.Clear();
    }

    private sealed record WorkspaceContext(string DataDirectory, string PackageDirectory, Func<Task> Synchronize) : IExtensionContext
    {
        public Task SyncWarehouseAsync() => Synchronize();
    }

    private static List<ExtensionPackage> OrderPackages(IEnumerable<ExtensionPackage> packages)
    {
        var available = packages.ToArray();
        List<string> savedOrder;
        try { savedOrder = JsonConvert.DeserializeObject<List<string>>(GlobalConfiguration.GetValue(NavigationOrderKey, "[]")) ?? []; }
        catch (JsonException) { savedOrder = []; }

        var byId = available.ToDictionary(package => package.Manifest.Id, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<ExtensionPackage>(available.Length);
        foreach (var id in savedOrder)
            if (byId.TryGetValue(id, out var package) && !ordered.Contains(package)) ordered.Add(package);
        ordered.AddRange(available.Where(package => !ordered.Contains(package)));
        return ordered;
    }

    private static HashSet<string> ReadIdSet(string key)
    {
        try
        {
            return new HashSet<string>(
                JsonConvert.DeserializeObject<List<string>>(GlobalConfiguration.GetValue(key, "[]")) ?? [],
                StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException) { return new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
    }

    private bool IsPackageEnabled(ExtensionPackage package) => !_disabledPackages.Contains(package.Manifest.Id);

    private void UpdateNavigationButton(ExtensionPackage package)
    {
        var id = package.Manifest.Id;
        var enabled = IsPackageEnabled(package);
        if (_navigationButtons.TryGetValue(id, out var button))
        {
            button.Classes.Set("inactive", !enabled);
            ToolTip.SetTip(button, enabled ? package.Manifest.Name : package.Manifest.Name + "（已停用，右键启用扩展）");
            Avalonia.Automation.AutomationProperties.SetName(button, enabled ? package.Manifest.Name : package.Manifest.Name + "（已停用）");
        }
        if (_navigationMenuItems.TryGetValue(id, out var menuItem))
            menuItem.Header = enabled ? "停用扩展" : "启用扩展";
    }

    private void TogglePackage(ExtensionPackage package)
    {
        var id = package.Manifest.Id;
        var disabling = IsPackageEnabled(package);
        if (disabling) _disabledPackages.Add(id);
        else _disabledPackages.Remove(id);
        UpdateNavigationButton(package);
        GlobalConfiguration.SetValue(DisabledPackagesKey, JsonConvert.SerializeObject(_disabledPackages.Order(StringComparer.OrdinalIgnoreCase)));

        if (disabling || !_loaded.TryGetValue(id, out var loaded)) return;
        try { loaded.Workspace.Update(id == HeldSwordsWorkspaceId ? _heldSwordsState : _state); }
        catch (Exception exception) { FailWorkspace(id, exception); }
    }

    private void ShowNoEnabledPackages()
    {
        WorkspaceContent.Children.Clear();
        WorkspaceContent.Children.Add(new Border
        {
            Padding = new Avalonia.Thickness(24),
            Child = new TextBlock
            {
                Text = "当前没有启用的扩展，可在左侧导航中右键启用。",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.Gray
            }
        });
    }

    private void NavigationPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(WorkspaceNavigation).Properties.IsLeftButtonPressed) return;
        var position = e.GetPosition(WorkspaceNavigation);
        var package = _packages.FirstOrDefault(item => _navigationButtons[item.Manifest.Id].Bounds.Contains(position));
        _dragCandidate = package?.Manifest.Id;
        _dragStart = position;
        _dragging = false;
        _navigationDragMoved = false;
        _dragOriginalIndex = package == null ? -1 : _packages.IndexOf(package);
        _dragTargetIndex = _dragOriginalIndex;
    }

    private void NavigationPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragCandidate is not { } id || !e.GetCurrentPoint(WorkspaceNavigation).Properties.IsLeftButtonPressed) return;
        var position = e.GetPosition(WorkspaceNavigation);
        if (!_dragging)
        {
            var dx = position.X - _dragStart.X;
            var dy = position.Y - _dragStart.Y;
            if (dx * dx + dy * dy < DragStartThreshold * DragStartThreshold) return;
            _dragging = true;
            e.Pointer.Capture(WorkspaceNavigation);
        }

        if (position.Y < 0 || position.Y > WorkspaceNavigation.Bounds.Height) return;
        var targetIndex = GetNavigationInsertionIndex(id, position.Y);
        if (targetIndex < 0) return;
        _dragTargetIndex = targetIndex;
        _navigationDragMoved = targetIndex != _dragOriginalIndex;
        UpdateNavigationAdorner(id, targetIndex);
        if (_navigationDragMoved) e.Handled = true;
    }

    private void NavigationPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var wasDragging = _dragging;
        if (_dragging && _dragCandidate is { } id && _navigationDragMoved && _dragTargetIndex >= 0)
        {
            ReorderNavigationItem(id, _dragTargetIndex);
            GlobalConfiguration.SetValue(NavigationOrderKey, JsonConvert.SerializeObject(_packages.Select(item => item.Manifest.Id)));
            _suppressNavigationClick = true;
            Dispatcher.UIThread.Post(() => _suppressNavigationClick = false, DispatcherPriority.Background);
        }
        ClearNavigationAdorner();
        _dragCandidate = null;
        _dragging = false;
        _navigationDragMoved = false;
        _dragOriginalIndex = _dragTargetIndex = -1;
        if (wasDragging) e.Pointer.Capture(null);
    }

    private void NavigationPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (ReferenceEquals(e.Pointer.Captured, WorkspaceNavigation)) return;
        if (!_dragging) return;
        ClearNavigationAdorner();
        _dragCandidate = null;
        _dragging = false;
        _navigationDragMoved = false;
        _dragOriginalIndex = _dragTargetIndex = -1;
    }

    private int GetNavigationInsertionIndex(string id, double pointerY)
    {
        if (!_navigationButtons.TryGetValue(id, out var draggedButton)) return -1;

        var insertionIndex = 0;
        foreach (var child in WorkspaceNavigation.Children)
        {
            if (ReferenceEquals(child, draggedButton)) continue;
            if (pointerY < child.Bounds.Y + child.Bounds.Height / 2) break;
            insertionIndex++;
        }
        return Math.Clamp(insertionIndex, 0, _packages.Count - 1);
    }

    private void UpdateNavigationAdorner(string id, int insertionIndex)
    {
        var layer = AdornerLayer.GetAdornerLayer(WorkspaceNavigation);
        if (layer == null || !_navigationButtons.TryGetValue(id, out var draggedButton)) return;
        var remaining = WorkspaceNavigation.Children.Where(child => !ReferenceEquals(child, draggedButton)).ToArray();
        if (remaining.Length == 0) return;
        var end = insertionIndex >= remaining.Length;
        var reference = remaining[end ? ^1 : insertionIndex];
        var origin = ((Visual)reference).TranslatePoint(new Avalonia.Point(0, 0), layer) ?? new Avalonia.Point();
        var y = origin.Y + (end ? reference.Bounds.Height : 0);
        if (_navigationAdorner == null)
        {
            _navigationAdorner = new DragAdorner(origin.X, reference.Bounds.Width, Brushes.DodgerBlue);
            layer.Children.Add(_navigationAdorner);
        }
        else
        {
            _navigationAdorner.UpdateXPosition(origin.X);
            _navigationAdorner.UpdateWidth(reference.Bounds.Width);
            if (!layer.Children.Contains(_navigationAdorner)) layer.Children.Add(_navigationAdorner);
        }
        _navigationAdorner.UpdatePosition(y, begin: insertionIndex == 0, end: end);
    }

    private void ClearNavigationAdorner()
    {
        if (_navigationAdorner == null) return;
        AdornerLayer.GetAdornerLayer(WorkspaceNavigation)?.Children.Remove(_navigationAdorner);
        _navigationAdorner = null;
    }

    private void ReorderNavigationItem(string id, int targetIndex)
    {
        var sourceIndex = _packages.FindIndex(package => string.Equals(package.Manifest.Id, id, StringComparison.OrdinalIgnoreCase));
        if (sourceIndex < 0 || sourceIndex == targetIndex || targetIndex < 0 || targetIndex >= _packages.Count) return;

        var package = _packages[sourceIndex];
        _packages.RemoveAt(sourceIndex);
        _packages.Insert(targetIndex, package);
        var button = _navigationButtons[id];
        WorkspaceNavigation.Children.Remove(button);
        WorkspaceNavigation.Children.Insert(targetIndex, button);
    }

    private async Task SyncWarehouseAsync(bool automatic)
    {
        if (_syncingWarehouse || _snapshot == null) return;
        _syncingWarehouse = true;
        var snapshot = _snapshot;
        var snapshotAt = snapshot.LatestResponseAt ?? _snapshotCollectedAt;
        try
        {
            var result = await Task.Run(() => HonmaruDataStoreService.SyncExtension(snapshot, snapshotAt));
            switch (result.Status)
            {
                case GameOverviewWarehouseSync.State.Saved:
                    _lastStaleWarning = "";
                    UpdateDataPersistenceService.NotifyWarehouseDataSaved();
                    if (!automatic) ToastHelper.Success("仓库同步", "仓库数据已同步并保存。");
                    break;
                case GameOverviewWarehouseSync.State.Unchanged:
                    if (!automatic) ToastHelper.Info("仓库同步", "没有新的资源数据，无需重复保存。");
                    break;
                case GameOverviewWarehouseSync.State.Stale:
                    var fingerprint = GameOverviewWarehouseSync.Fingerprint(snapshot);
                    if (!automatic || _lastStaleWarning != fingerprint) ToastHelper.Warn("仓库同步已拒绝", "游戏资源数据早于仓库最新记录，请先同步游戏数据。", 8);
                    _lastStaleWarning = fingerprint;
                    break;
                default:
                    if (!automatic) ToastHelper.Warn("无法同步仓库", "没有可确认的资源数据，请先同步游戏数据。");
                    break;
            }
        }
        catch (Exception exception)
        {
            ToastHelper.Warn("仓库同步失败", "未能保存，请检查本丸数据目录权限。");
            LoggerHelper.Warning("扩展仓库同步失败，类型：" + exception.GetType().Name);
        }
        finally
        {
            var seconds = int.TryParse(GlobalConfiguration.GetValue("Extension.WarehouseIntervalSeconds", "60"), out var interval) ? Math.Clamp(interval, 1, 86400) : 60;
            _nextWarehouseSync = DateTime.UtcNow.AddSeconds(seconds);
            _syncingWarehouse = false;
        }
    }

    private async void ReadData(object? sender, RoutedEventArgs e) => await ReadAsync(automatic: false);

    private void TickWorkspaces()
    {
        foreach (var entry in _loaded.ToArray())
        {
            if (_disabledPackages.Contains(entry.Key)) continue;
            try { entry.Value.Workspace.Tick(DateTimeOffset.UtcNow); }
            catch (Exception exception) { FailWorkspace(entry.Key, exception); }
        }
    }

    private async Task PollAsync()
    {
        TickWorkspaces();
        var enabled = IsAutomaticReadEnabled();
        var device = CurrentDeviceKey();
        if (_displayedDevice.Length > 0 && device != _displayedDevice)
        {
            _snapshot = null;
            LastReadText.Text = "尚未同步";
            PublishState("设备已切换");
            _displayedDevice = "";
            _nextRead = DateTime.MinValue;
        }
        var stopped = !enabled && _automaticWasEnabled;
        _automaticWasEnabled = enabled;
        if (stopped) await GameOverviewReader.Instance.StopAsync();
        if (enabled && !_reading && DateTime.UtcNow >= _nextRead) await ReadAsync(automatic: true);
        var autoWarehouse = bool.TryParse(GlobalConfiguration.GetValue("Extension.AutoWarehouseSyncEnabled", "False"), out var warehouseEnabled) && warehouseEnabled;
        if (autoWarehouse && !_warehouseAutomaticWasEnabled) _nextWarehouseSync = DateTime.MinValue;
        _warehouseAutomaticWasEnabled = autoWarehouse;
        if (autoWarehouse && _snapshot != null && !_syncingWarehouse && DateTime.UtcNow >= _nextWarehouseSync) await SyncWarehouseAsync(true);
    }

    private static bool IsAutomaticReadEnabled() =>
        bool.TryParse(GlobalConfiguration.GetValue("Extension.AutoReadEnabled", "False"), out var enabled) && enabled;

    private static string CurrentDeviceKey()
    {
        var device = Instances.InstanceTabBarViewModel.ActiveTab?.TaskQueueViewModel?.Processor.Config.AdbDevice;
        return device == null ? "" : device.AdbPath + "|" + device.AdbSerial;
    }

    private async Task ReadAsync(bool automatic)
    {
        if (_reading) return;
        _reading = true;
        ReadDataButton.IsEnabled = false;
        ReadStatusText.Text = "正在读取日志…";
        try
        {
            var taskQueue = Instances.InstanceTabBarViewModel.ActiveTab?.TaskQueueViewModel;
            if (taskQueue?.CurrentController != MaaControllerTypes.Adb) throw new InvalidOperationException("数据读取仅支持当前选择的 ADB 模拟器。");
            var device = taskQueue.Processor.Config.AdbDevice;
            if (device == null || string.IsNullOrWhiteSpace(device.AdbSerial) || string.IsNullOrWhiteSpace(device.AdbPath)) throw new InvalidOperationException("请先在主页选择 ADB 设备。");
            var key = CurrentDeviceKey();
            var keepRoot = IsAutomaticReadEnabled();
            _automaticWasEnabled |= keepRoot;
            var snapshot = await GameOverviewReader.Instance.ReadAsync(device.AdbPath, device.AdbSerial, keepRoot);
            if (key != CurrentDeviceKey())
            {
                _snapshot = null;
                PublishState("设备已切换");
                ReadStatusText.Text = "设备已切换，请重新同步。";
                return;
            }
            var accountChanged = _snapshot != null && _snapshot.Account != snapshot.Account;
            _snapshot = snapshot;
            _snapshotCollectedAt = DateTimeOffset.Now;
            _displayedDevice = key;
            LastReadText.Text = _snapshotCollectedAt.Value.ToLocalTime().ToString("MM-dd HH:mm:ss");
            PublishState("最近一次读取的账号状态", refreshHeldSwords: true,
                clearHeldSwords: automatic && accountChanged);
            try
            {
                await Task.Run(() => new GameOverviewDataStore(_configDirectory).Save(snapshot, _snapshotCollectedAt));
                ReadStatusText.Text = "";
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                ReadStatusText.Text = "读取成功，但保存失败，请检查本丸数据目录权限。";
                LoggerHelper.Warning("扩展快照保存失败，类型：" + exception.GetType().Name);
            }
        }
        catch (Exception exception)
        {
            ReadStatusText.Text = exception is InvalidOperationException ? exception.Message : "读取失败，请检查模拟器连接。";
            PublishState("读取失败，保留上次结果", refreshHeldSwords: false);
            LoggerHelper.Warning("扩展游戏数据读取失败，类型：" + exception.GetType().Name);
        }
        finally
        {
            var seconds = int.TryParse(GlobalConfiguration.GetValue("Extension.ReadIntervalSeconds", "5"), out var interval) ? Math.Clamp(interval, 1, 86400) : 5;
            _nextRead = DateTime.UtcNow.AddSeconds(seconds);
            _reading = false;
            ReadDataButton.IsEnabled = true;
        }
    }

    private void LoadSavedSnapshot()
    {
        try
        {
            var saved = new GameOverviewDataStore(_configDirectory).LoadLatest();
            if (saved == null) return;
            _snapshot = saved.Snapshot;
            _snapshotCollectedAt = saved.CollectedAt;
            LastReadText.Text = saved.CollectedAt.ToLocalTime().ToString("MM-dd HH:mm:ss");
            PublishState("上次保存的数据");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            ReadStatusText.Text = "已保存数据加载失败，请重新同步。";
            LoggerHelper.Warning("扩展快照加载失败，类型：" + exception.GetType().Name);
        }
    }

    private void ToggleNavigation(object? sender, RoutedEventArgs e)
    {
        _navigationExpanded = !_navigationExpanded;
        NavigationPane.Width = _navigationExpanded ? 174 : 58;
        NavigationHeading.IsVisible = SyncButtonLabel.IsVisible = SyncDetails.IsVisible = SyncDivider.IsVisible = _navigationExpanded;
        foreach (var label in _navigationLabels) label.IsVisible = _navigationExpanded;
        NavigationToggleIcon.RenderTransform = new RotateTransform(_navigationExpanded ? 0 : 180);
        var description = _navigationExpanded ? "收起功能导航" : "展开功能导航";
        ToolTip.SetTip(NavigationToggle, description);
        Avalonia.Automation.AutomationProperties.SetName(NavigationToggle, description);
    }
}
