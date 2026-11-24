using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Services;
using MFAAvalonia.Views.UserControls;
using Newtonsoft.Json;

namespace MFAAvalonia.Views.Pages;

/// <summary>承载 MATR 内置本丸扩展页面，并连接可选游戏日志读取器。</summary>
public partial class ExtensionOverviewView : UserControl
{
    private sealed record Entry(string Id, string Name, FluentIcons.Common.Icon Icon);
    private static readonly Entry[] Entries =
    [
        new("overview", "状态总览", FluentIcons.Common.Icon.DataBarVertical),
        new("teams", "部队状况", FluentIcons.Common.Icon.PeopleTeam),
        new("swords", "所持刀剑", FluentIcons.Common.Icon.List),
        new("harvest", "收获记录", FluentIcons.Common.Icon.History)
    ];

    private readonly Dictionary<string, Control> _views = new(StringComparer.Ordinal);
    private readonly List<TextBlock> _navigationLabels = [];
    private readonly IGameLogReader? _reader;
    private readonly string _configDirectory = AppPaths.ConfigDirectory;
    private readonly DispatcherTimer _readTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private GameOverviewSnapshot? _snapshot;
    private DateTimeOffset? _snapshotCollectedAt;
    private string _selected = "";
    private bool _navigationExpanded = true;
    private bool _reading;
    private bool _syncingWarehouse;
    private bool _automaticWasEnabled;
    private bool _warehouseAutomaticWasEnabled;
    private DateTime _nextRead = DateTime.MinValue;
    private DateTime _nextWarehouseSync = DateTime.MinValue;
    private string _displayedDevice = "";
    private string _lastStaleWarning = "";

    public ExtensionOverviewView()
    {
        InitializeComponent();
        _reader = GameLogReaderPluginService.GetReader();
        if (_reader == null) return;
        foreach (var entry in Entries)
        {
            var label = new TextBlock { Text = entry.Name, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            _navigationLabels.Add(label);
            var content = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 10 };
            content.Children.Add(new FluentIcons.Avalonia.Fluent.FluentIcon { Icon = entry.Icon, IconSize = FluentIcons.Common.IconSize.Size20 });
            content.Children.Add(label);
            var button = new RadioButton { Content = content, GroupName = "ExtensionNavigation" };
            button.Classes.Add("extension-navigation");
            Avalonia.Automation.AutomationProperties.SetName(button, entry.Name);
            button.Click += (_, _) => SelectPage(entry.Id);
            WorkspaceNavigation.Children.Add(button);
        }

        SelectPage(Entries[0].Id);
        LoadSavedSnapshot();
        _readTimer.Tick += async (_, _) => await PollAsync();
        _readTimer.Start();
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IControlledApplicationLifetime lifetime)
            lifetime.Exit += async (_, _) => await StopReaderAsync();
    }

    private void SelectPage(string id)
    {
        if (!_views.TryGetValue(id, out var view))
        {
            view = id switch
            {
                "overview" => new DefaultOverviewView(() => SyncWarehouseAsync(false)),
                "teams" => new TeamStatusView(),
                "swords" => new HeldSwordsView(),
                "harvest" => new HarvestRecordsView(),
                _ => throw new InvalidOperationException("未知的本丸扩展页面：" + id)
            };
            _views[id] = view;
            UpdatePage(view, "等待读取游戏数据");
        }

        _selected = id;
        WorkspaceContent.Children.Clear();
        WorkspaceContent.Children.Add(view);
        var index = Array.FindIndex(Entries, entry => entry.Id == id);
        if (index >= 0 && WorkspaceNavigation.Children[index] is RadioButton button)
            button.IsChecked = true;
    }

    private void UpdatePage(Control view, string status)
    {
        switch (view)
        {
            case DefaultOverviewView overview:
                overview.Update(_snapshot, status);
                break;
            case TeamStatusView teams:
                if (_snapshot == null) teams.ClearSnapshot(); else teams.SetSnapshot(_snapshot);
                break;
            case HeldSwordsView swords:
                if (_snapshot == null) swords.ClearSnapshot(); else swords.SetSnapshot(_snapshot);
                break;
            case HarvestRecordsView harvest when _snapshot != null:
                harvest.AddRecords(_snapshot.Account, _snapshot.HarvestRecords);
                harvest.AddDutyRecords(_snapshot.Account, _snapshot.DutyRecords);
                break;
        }
    }

    private void UpdatePages(string status)
    {
        foreach (var view in _views.Values) UpdatePage(view, status);
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
            LoggerHelper.Warning("本丸扩展仓库同步失败，类型：" + exception.GetType().Name);
        }
        finally
        {
            var seconds = int.TryParse(GlobalConfiguration.GetValue("Extension.WarehouseIntervalSeconds", "60"), out var interval) ? Math.Clamp(interval, 1, 86400) : 60;
            _nextWarehouseSync = DateTime.UtcNow.AddSeconds(seconds);
            _syncingWarehouse = false;
        }
    }

    private async void ReadData(object? sender, RoutedEventArgs e) => await ReadAsync(false);

    private async Task PollAsync()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var view in _views.Values)
        {
            if (view is DefaultOverviewView overview) overview.Tick(now);
            else if (view is TeamStatusView teams) teams.Tick(now);
        }

        var enabled = bool.TryParse(GlobalConfiguration.GetValue("Extension.AutoReadEnabled", "False"), out var readEnabled) && readEnabled;
        var device = CurrentDeviceKey();
        if (_displayedDevice.Length > 0 && device != _displayedDevice)
        {
            _snapshot = null;
            LastReadText.Text = "尚未同步";
            UpdatePages("设备已切换");
            _displayedDevice = "";
            _nextRead = DateTime.MinValue;
        }

        var stopped = !enabled && _automaticWasEnabled;
        _automaticWasEnabled = enabled;
        if (stopped && _reader != null) await _reader.StopAsync();
        if (enabled && !_reading && DateTime.UtcNow >= _nextRead) await ReadAsync(true);

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
            var keepRoot = bool.TryParse(GlobalConfiguration.GetValue("Extension.AutoReadEnabled", "False"), out var enabled) && enabled;
            _automaticWasEnabled |= keepRoot;
            var snapshot = await (_reader ?? throw new InvalidOperationException("游戏日志读取器不可用。"))
                .ReadAsync(device.AdbPath, device.AdbSerial, keepRoot);
            if (key != CurrentDeviceKey())
            {
                _snapshot = null;
                UpdatePages("设备已切换");
                ReadStatusText.Text = "设备已切换，请重新同步。";
                return;
            }
            _snapshot = snapshot;
            _snapshotCollectedAt = DateTimeOffset.Now;
            _displayedDevice = key;
            LastReadText.Text = _snapshotCollectedAt.Value.ToLocalTime().ToString("MM-dd HH:mm:ss");
            UpdatePages("最近一次读取的账号状态");
            try
            {
                await Task.Run(() => new GameOverviewDataStore(_configDirectory).Save(snapshot, _snapshotCollectedAt));
                ReadStatusText.Text = "";
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                ReadStatusText.Text = "读取成功，但保存失败，请检查本丸数据目录权限。";
                LoggerHelper.Warning("本丸快照保存失败，类型：" + exception.GetType().Name);
            }
        }
        catch (Exception exception)
        {
            ReadStatusText.Text = exception is InvalidOperationException ? exception.Message : "读取失败，请检查模拟器连接。";
            UpdatePages("读取失败，保留上次结果");
            LoggerHelper.Warning("游戏数据读取失败，类型：" + exception.GetType().Name);
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
            UpdatePages("上次保存的数据");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            ReadStatusText.Text = "已保存数据加载失败，请重新同步。";
            LoggerHelper.Warning("本丸快照加载失败，类型：" + exception.GetType().Name);
        }
    }

    private async Task StopReaderAsync()
    {
        _readTimer.Stop();
        if (_reader == null) return;
        try { await _reader.StopAsync(); }
        catch (Exception exception) { LoggerHelper.Warning("停止游戏日志读取器失败：" + exception.GetType().Name); }
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
