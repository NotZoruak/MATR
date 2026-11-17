using System;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using MFAAvalonia.Configuration;

namespace MFAAvalonia.ViewModels.UsersControls.Settings;

/// <summary>扩展同步偏好，采集服务接入后按这些配置运行。</summary>
public partial class ExtensionSettingsViewModel : ViewModelBase
{
    private const string ReadEnabledKey = "Extension.AutoReadEnabled";
    private const string ReadIntervalKey = "Extension.ReadIntervalSeconds";
    private const string WarehouseEnabledKey = "Extension.AutoWarehouseSyncEnabled";
    private const string WarehouseIntervalKey = "Extension.WarehouseIntervalSeconds";

    [ObservableProperty]
    private bool _autoReadEnabled = ReadFlag(ReadEnabledKey);

    [ObservableProperty]
    private decimal _readIntervalSeconds = ReadInterval(ReadIntervalKey, 5);

    [ObservableProperty]
    private bool _autoWarehouseSyncEnabled = ReadFlag(WarehouseEnabledKey);

    [ObservableProperty]
    private decimal _warehouseIntervalSeconds = ReadInterval(WarehouseIntervalKey, 60);

    partial void OnAutoReadEnabledChanged(bool value) => GlobalConfiguration.SetValue(ReadEnabledKey, value.ToString());
    partial void OnAutoWarehouseSyncEnabledChanged(bool value) => GlobalConfiguration.SetValue(WarehouseEnabledKey, value.ToString());
    partial void OnReadIntervalSecondsChanged(decimal value) => SaveInterval(ReadIntervalKey, value);
    partial void OnWarehouseIntervalSecondsChanged(decimal value) => SaveInterval(WarehouseIntervalKey, value);

    private static bool ReadFlag(string key) => bool.TryParse(GlobalConfiguration.GetValue(key, "False"), out var value) && value;

    private static decimal ReadInterval(string key, decimal fallback) =>
        decimal.TryParse(GlobalConfiguration.GetValue(key, ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(decimal.Truncate(value), 1, 86400)
            : fallback;

    private static void SaveInterval(string key, decimal value) =>
        GlobalConfiguration.SetValue(key, Math.Clamp(decimal.Truncate(value), 1, 86400).ToString(CultureInfo.InvariantCulture));
}
