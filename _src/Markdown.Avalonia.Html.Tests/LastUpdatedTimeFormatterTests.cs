using System;
using MFAAvalonia.Services;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public sealed class LastUpdatedTimeFormatterTests
{
    [Fact]
    public void 未保存数据时显示暂无更新时间()
    {
        Assert.Equal("最后更新：暂无", LastUpdatedTimeFormatter.Format(null));
    }

    [Fact]
    public void 已保存数据时按固定格式显示更新时间()
    {
        Assert.Equal("最后更新：2026-10-01 14:30", LastUpdatedTimeFormatter.Format(new DateTime(2026, 10, 1, 14, 30, 0)));
    }

    [Fact]
    public void 自动识别说明与更新时间合并为同一段文本()
    {
        Assert.Equal(
            "使用自动识别时，请保证游戏页面右上角能识别到目录按钮。最后更新：暂无",
            LastUpdatedTimeFormatter.FormatHint("使用自动识别时，请保证游戏页面右上角能识别到目录按钮。", "最后更新：暂无"));
    }
}
