using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.Pages;
using System;
using System.Reflection;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class TaskRecoveryMonitorTests
{
    [Fact]
    public void 重连宽限期内即使暂无新帧也不应重建截图通道()
    {
        using var viewModel = new TaskQueueViewModel("live-view-grace-test");
        var type = typeof(TaskQueueViewModel);
        type.GetField("_lastConnectedAtUtc", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(viewModel, DateTime.UtcNow - TimeSpan.FromSeconds(6));
        type.GetField("_liveViewLastFrameAtUtc", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(viewModel, null);

        var shouldRebuild = (bool)type
            .GetMethod("ShouldRebuildStaleLiveViewChannel", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(viewModel, null)!;

        Assert.False(shouldRebuild);
    }

    [Theory]
    [InlineData("模拟器无响应：超过 120 秒没有任务回调", true)]
    [InlineData("画面冻结：重复点击 S_DetectWhereAmI，坐标 (100,200)", false)]
    [InlineData("动作循环卡死：重复点击 S_DetectWhereAmI", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void 只有无回调形态才判定为模拟器无响应(string? reason, bool expected)
    {
        Assert.Equal(expected, TaskRecoveryMonitor.IsEmulatorUnresponsiveReason(reason));
    }

    [Theory]
    [InlineData("画面冻结：重复点击 S_DetectWhereAmI，坐标 (100,200)", "动作循环：重复点击 S_DetectWhereAmI，坐标 (100,200)")]
    [InlineData("动作循环卡死：重复点击 S_DetectWhereAmI", "动作循环：重复点击 S_DetectWhereAmI")]
    [InlineData("模拟器无响应：超过 120 秒没有任务回调", "模拟器无响应：超过 120 秒没有任务回调")]
    public void 卡死原因展示文案应把动作循环简化并保留细节(string reason, string expected)
    {
        Assert.Equal(expected, TaskRecoveryMonitor.GetDisplayReason(reason));
    }
}
