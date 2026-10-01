using MFAAvalonia.Configuration;
using System;
using System.Globalization;

namespace MFAAvalonia.Services;

/// <summary>记录并读取仓库与刀帐正式数据的最后保存时间。</summary>
public static class DataLastUpdatedService
{
    /// <summary>记录仓库正式数据的保存时间。</summary>
    public static void MarkWarehouseUpdated() => MarkUpdated(ConfigurationKeys.WarehouseLastUpdatedAt);

    /// <summary>记录刀帐正式数据的保存时间。</summary>
    public static void MarkSwordBookUpdated() => MarkUpdated(ConfigurationKeys.SwordBookLastUpdatedAt);

    /// <summary>获取仓库页面显示的最后更新时间。</summary>
    public static string GetWarehouseLastUpdatedText() => GetLastUpdatedText(ConfigurationKeys.WarehouseLastUpdatedAt);

    /// <summary>获取刀帐页面显示的最后更新时间。</summary>
    public static string GetSwordBookLastUpdatedText() => GetLastUpdatedText(ConfigurationKeys.SwordBookLastUpdatedAt);

    private static void MarkUpdated(string key)
    {
        ConfigurationManager.Current.SetValue(key, DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
    }

    private static string GetLastUpdatedText(string key)
    {
        var value = ConfigurationManager.Current.GetValue(key, string.Empty);
        var updatedAt = DateTime.TryParseExact(
            value,
            "O",
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : (DateTime?)null;
        return LastUpdatedTimeFormatter.Format(updatedAt);
    }
}
