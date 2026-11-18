using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MATR.ExtensionSdk;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Services;
using Newtonsoft.Json;

namespace MFAAvalonia.Views.Pages;

/// <summary>独立扩展的导航与共享数据宿主，页面内容完全由包提供。</summary>
public partial class ExtensionOverviewView : UserControl
{
    private bool _navigationExpanded = true;
    private GameOverviewLogParser.Snapshot? _snapshot;
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
    private readonly DispatcherTimer _readTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Dictionary<string, LoadedExtension> _loaded = new();
    private readonly Dictionary<string, Control> _views = new();
    private readonly List<TextBlock> _navigationLabels = [];

    private readonly ExtensionPackageCatalog _catalog;
    private readonly string _configDirectory;

    public ExtensionOverviewView() : this(ExtensionContentService.Content, AppPaths.ConfigDirectory, true) { }

    public ExtensionOverviewView(ExtensionPackageCatalog catalog, string configDirectory, bool startServices = false)
    {
        _catalog = catalog;
        _configDirectory = configDirectory;
        InitializeComponent();
        foreach (var package in _catalog.Packages)
        {
            var label = new TextBlock { Text = package.Manifest.Name, VerticalAlignment = VerticalAlignment.Center };
            _navigationLabels.Add(label);
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            var icon = Enum.TryParse<FluentIcons.Common.Icon>(package.Manifest.Icon, out var parsed) ? parsed : FluentIcons.Common.Icon.Apps;
            content.Children.Add(new FluentIcons.Avalonia.Fluent.FluentIcon { Icon = icon, IconSize = FluentIcons.Common.IconSize.Size20 });
            content.Children.Add(label);
            var button = new RadioButton { Content = content, GroupName = "ExtensionNavigation" };
            button.Classes.Add("extension-navigation");
            ToolTip.SetTip(button, package.Manifest.Name);
            Avalonia.Automation.AutomationProperties.SetName(button, package.Manifest.Name);
            button.Click += (_, _) => SelectWorkspace(package);
            WorkspaceNavigation.Children.Add(button);
        }
        if (WorkspaceNavigation.Children.FirstOrDefault() is RadioButton first)
        {
            first.IsChecked = true;
        }
        WorkspaceContent.AttachedToVisualTree += (_, _) =>
        {
            if (_selected.Length == 0 && _catalog.Packages.Count > 0)
                SelectWorkspace(_catalog.Packages[0]);
        };
        if (startServices && _catalog.Packages.Count > 0)
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
                loaded.Workspace.Update(_state);
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
        if (_loaded.Remove(id, out var loaded))
            try { loaded.Dispose(); } catch (Exception disposeError) { LoggerHelper.Warning("释放扩展失败：" + disposeError.GetType().Name); }
        var view = new Border { Padding = new Avalonia.Thickness(20), Child = new TextBlock { Text = "此扩展未能加载或运行，请检查扩展包与 MATR 的接口版本。\n扩展：" + id + "\n错误类型：" + exception.GetType().Name, TextWrapping = TextWrapping.Wrap } };
        _views[id] = view;
        if (_selected == id)
            try { MountWorkspace(view); }
            catch (Exception detachError) { LoggerHelper.Warning("替换扩展错误页面失败：" + detachError.GetType().Name); }
    }

    private void PublishState(string status)
    {
        _state = new ExtensionState(_snapshot == null ? null : JsonConvert.SerializeObject(_snapshot), status);
        foreach (var entry in _loaded.ToArray())
            try { entry.Value.Workspace.Update(_state); }
            catch (Exception exception) { FailWorkspace(entry.Key, exception); }
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

    private async Task SyncWarehouseAsync(bool automatic)
    {
        if (_syncingWarehouse || _snapshot == null) return;
        _syncingWarehouse = true;
        var snapshot = _snapshot;
        try
        {
            var result = await Task.Run(() => HonmaruDataStoreService.SyncExtension(snapshot));
            switch (result.Status)
            {
                case GameOverviewWarehouseSync.State.Saved:
                    _lastStaleWarning = "";
                    UpdateDataPersistenceService.NotifyWarehouseDataSaved();
                    if (!automatic) ToastHelper.Success("仓库同步", "资源已同步并保存。");
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

    private async void ReadData(object? sender, RoutedEventArgs e) => await ReadAsync();

    private void TickWorkspaces()
    {
        foreach (var entry in _loaded.ToArray())
            try { entry.Value.Workspace.Tick(DateTimeOffset.UtcNow); }
            catch (Exception exception) { FailWorkspace(entry.Key, exception); }
    }

    private async Task PollAsync()
    {
        TickWorkspaces();
        var enabled = bool.TryParse(GlobalConfiguration.GetValue("Extension.AutoReadEnabled", "False"), out var value) && value;
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
        if (enabled && !_reading && DateTime.UtcNow >= _nextRead) await ReadAsync();
        var autoWarehouse = bool.TryParse(GlobalConfiguration.GetValue("Extension.AutoWarehouseSyncEnabled", "False"), out var warehouseEnabled) && warehouseEnabled;
        if (autoWarehouse && !_warehouseAutomaticWasEnabled) _nextWarehouseSync = DateTime.MinValue;
        _warehouseAutomaticWasEnabled = autoWarehouse;
        if (autoWarehouse && _snapshot != null && !_syncingWarehouse && DateTime.UtcNow >= _nextWarehouseSync) await SyncWarehouseAsync(true);
    }

    private static string CurrentDeviceKey()
    {
        var device = Instances.InstanceTabBarViewModel.ActiveTab?.TaskQueueViewModel?.Processor.Config.AdbDevice;
        return device == null ? "" : device.AdbPath + "|" + device.AdbSerial;
    }

    private async Task ReadAsync()
    {
        if (_reading) return;
        _reading = true;
        ReadDataButton.IsEnabled = false;
        ReadStatusText.Text = "正在读取游戏日志…";
        try
        {
            var taskQueue = Instances.InstanceTabBarViewModel.ActiveTab?.TaskQueueViewModel;
            if (taskQueue?.CurrentController != MaaControllerTypes.Adb) throw new InvalidOperationException("数据读取仅支持当前选择的 ADB 模拟器。");
            var device = taskQueue.Processor.Config.AdbDevice;
            if (device == null || string.IsNullOrWhiteSpace(device.AdbSerial) || string.IsNullOrWhiteSpace(device.AdbPath)) throw new InvalidOperationException("请先在主页选择 ADB 设备。");
            var key = CurrentDeviceKey();
            var snapshot = await GameOverviewReader.Instance.ReadAsync(device.AdbPath, device.AdbSerial);
            if (key != CurrentDeviceKey())
            {
                _snapshot = null;
                PublishState("设备已切换");
                ReadStatusText.Text = "设备已切换，请重新同步。";
                return;
            }
            _snapshot = snapshot;
            _displayedDevice = key;
            LastReadText.Text = DateTime.Now.ToString("MM-dd HH:mm:ss");
            PublishState("最近一次读取的账号状态");
            try
            {
                await Task.Run(() => new GameOverviewDataStore(_configDirectory).Save(snapshot));
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
            PublishState("读取失败，保留上次结果");
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
        NavigationHeading.IsVisible = SyncButtonLabel.IsVisible = SyncDetails.IsVisible = _navigationExpanded;
        foreach (var label in _navigationLabels) label.IsVisible = _navigationExpanded;
        NavigationToggleIcon.RenderTransform = new RotateTransform(_navigationExpanded ? 0 : 180);
        var description = _navigationExpanded ? "收起功能导航" : "展开功能导航";
        ToolTip.SetTip(NavigationToggle, description);
        Avalonia.Automation.AutomationProperties.SetName(NavigationToggle, description);
    }
}
