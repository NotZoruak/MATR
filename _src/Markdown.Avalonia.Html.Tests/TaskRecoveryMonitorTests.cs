using MFAAvalonia.Helper;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class TaskRecoveryMonitorTests
{
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
