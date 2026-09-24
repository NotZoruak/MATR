using MFAAvalonia.Services;
using System;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public sealed class WarehouseComparisonChartTests
{
    [Fact]
    public void 初始化时默认选中四种核心资源()
    {
        var selection = new WarehouseComparisonResourceSelection();

        Assert.Equal(
            ["木炭", "玉钢", "冷却材", "砥石"],
            selection.SelectedResourceNames);
    }

    [Fact]
    public void 切换资源后只返回当前选中的资源()
    {
        var selection = new WarehouseComparisonResourceSelection();

        selection.Toggle("玉钢");
        selection.Toggle("砥石");

        Assert.Equal(["木炭", "冷却材"], selection.SelectedResourceNames);
    }

    [Fact]
    public void 不允许取消最后一个选中的资源()
    {
        var selection = new WarehouseComparisonResourceSelection();

        selection.Toggle("木炭");
        selection.Toggle("玉钢");
        selection.Toggle("冷却材");
        selection.Toggle("砥石");

        Assert.Equal(["砥石"], selection.SelectedResourceNames);
    }

    [Fact]
    public void 不允许选择四种资源之外的名称()
    {
        var selection = new WarehouseComparisonResourceSelection();

        selection.Toggle("小判");

        Assert.Equal(
            ["木炭", "玉钢", "冷却材", "砥石"],
            selection.SelectedResourceNames);
    }

    [Fact]
    public void 对比图选择两个时间后形成一个时间范围()
    {
        var selection = new WarehouseComparisonSelection();
        var first = new DateTime(2026, 9, 17, 8, 0, 0);
        var second = first.AddHours(6);

        Assert.False(selection.Select(first));
        Assert.True(selection.Select(second));
        Assert.Equal(first, selection.Start);
        Assert.Equal(second, selection.End);
        Assert.Equal(TimeSpan.FromHours(6), selection.Duration);
    }

    [Fact]
    public void 完成一个范围后再次点击会开始新的范围()
    {
        var selection = new WarehouseComparisonSelection();
        var first = new DateTime(2026, 9, 17, 8, 0, 0);
        var second = first.AddHours(6);
        var third = first.AddDays(1);

        selection.Select(first);
        selection.Select(second);
        Assert.False(selection.Select(third));
        Assert.Equal(third, selection.Start);
        Assert.Null(selection.End);
    }
}
