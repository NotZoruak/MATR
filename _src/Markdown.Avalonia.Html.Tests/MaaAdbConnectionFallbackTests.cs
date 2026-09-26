using MaaFramework.Binding;
using MFAAvalonia.Extensions.MaaFW;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class MaaAdbConnectionFallbackTests
{
    [Fact]
    public void EmulatorExtras失败时应回退到Default而其他截图方式保持不变()
    {
        Assert.Equal(
            AdbScreencapMethods.Default,
            MaaAdbConnectionFallback.GetFallbackScreenCap(AdbScreencapMethods.EmulatorExtras));
        Assert.Equal(
            AdbScreencapMethods.RawWithGzip,
            MaaAdbConnectionFallback.GetFallbackScreenCap(AdbScreencapMethods.RawWithGzip));
        Assert.Equal(
            AdbScreencapMethods.Default,
            MaaAdbConnectionFallback.GetFallbackScreenCap(AdbScreencapMethods.All));
    }
}
