using MFAAvalonia.Extensions.MaaFW.Custom;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class ExpeditionTimeTrackerTests
{
    [Theory]
    [InlineData("(14:04:00", 50640)]
    [InlineData("14:04:00)", 50640)]
    [InlineData("(14:04:00)", 50640)]
    [InlineData("14:04:00", 50640)]
    [InlineData("（14：04：00", 50640)]
    [InlineData("14：04：00）", 50640)]
    public void 应从括号不完整或没有括号的内番时间中提取秒数(string text, int expectedSeconds)
    {
        Assert.Equal(expectedSeconds, ExpeditionTimeTracker.ParseRemainingSeconds(text));
    }

    [Fact]
    public void 开启内番时应使用远征和内番中较早的完成时间()
    {
        var result = ExpeditionTimeTracker.GetEarliestRemainingSeconds([3600, 900, null], 1200, true, true);

        Assert.Equal(900, result);
    }

    [Fact]
    public void 未开启内番时应忽略内番剩余时间()
    {
        var result = ExpeditionTimeTracker.GetEarliestRemainingSeconds([3600, null], 1200, true, false);

        Assert.Equal(3600, result);
    }

    [Fact]
    public void 没有进行中远征时仍应使用已开启内番的剩余时间()
    {
        var result = ExpeditionTimeTracker.GetEarliestRemainingSeconds([null, null], 1200, false, true);

        Assert.Equal(1200, result);
    }

    [Fact]
    public void 关闭远征时不应把部队剩余时间纳入比较()
    {
        var result = ExpeditionTimeTracker.GetEarliestRemainingSeconds([900, 1200], 3600, false, true);

        Assert.Equal(3600, result);
    }

    [Fact]
    public void 远征和内番都关闭时不应返回剩余时间()
    {
        var result = ExpeditionTimeTracker.GetEarliestRemainingSeconds([900, 1200], 3600, false, false);

        Assert.Null(result);
    }
}
