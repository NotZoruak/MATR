using System;

namespace MFAAvalonia.Services;

/// <summary>格式化数据页面的最后更新时间显示文本。</summary>
public static class LastUpdatedTimeFormatter
{
    /// <summary>将保存时间格式化为页面提示文本。</summary>
    public static string Format(DateTime? updatedAt) => updatedAt.HasValue
        ? $"最后更新：{updatedAt.Value:yyyy-MM-dd HH:mm}"
        : "最后更新：暂无";

    /// <summary>将自动识别说明与最后更新时间合并为同一段文本。</summary>
    public static string FormatHint(string instruction, string lastUpdatedText) => $"{instruction}{lastUpdatedText}";
}
