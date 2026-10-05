using MFAAvalonia.Extensions.MaaFW.Custom;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class FatigueCheckRecognitionTests
{
    [Fact]
    public void 存在成员低于阈值时应命中刷花条件()
    {
        Assert.True(FatigueCheckRecognition.ShouldBrush([95, 90, null, 100], 91));
    }

    [Fact]
    public void 等于或高于阈值时不应命中刷花条件()
    {
        Assert.False(FatigueCheckRecognition.ShouldBrush([91, 100, null], 91));
    }

    [Fact]
    public void 全部疲劳值无法识别时应视为达标()
    {
        Assert.False(FatigueCheckRecognition.ShouldBrush([null, null, null], 91));
    }
}
