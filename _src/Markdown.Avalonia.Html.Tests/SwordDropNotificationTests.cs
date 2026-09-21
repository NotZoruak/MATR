using MFAAvalonia.Extensions.MaaFW.Custom;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class SwordDropNotificationTests
{
    [Fact]
    public void 应使用统一文本作为系统通知和实时日志内容()
    {
        var message = SwordDropNotificationMatcher.BuildNotificationMessage("太刀", "三日月宗近");

        Assert.Equal("获得 太刀「三日月宗近」", message);
    }
}
