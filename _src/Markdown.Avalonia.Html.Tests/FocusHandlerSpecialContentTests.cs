using MFAAvalonia.Extensions.MaaFW;
using System.Reflection;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class FocusHandlerSpecialContentTests
{
    [Fact]
    public void TryExtractSpecialContent_移除标记并返回特殊状态()
    {
        var method = typeof(FocusHandler).GetMethod(
            "TryExtractSpecialContent",
            BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(method);

        object?[] arguments = ["special:[日课] 锻刀已跳过", null];
        var isSpecial = Assert.IsType<bool>(method.Invoke(null, arguments));

        Assert.True(isSpecial);
        Assert.Equal("[日课] 锻刀已跳过", Assert.IsType<string>(arguments[1]));
    }

    [Fact]
    public void TryExtractSpecialContent_普通文本保持原样且不标记特殊状态()
    {
        var method = typeof(FocusHandler).GetMethod(
            "TryExtractSpecialContent",
            BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(method);

        object?[] arguments = ["[日课] 锻刀完成", null];
        var isSpecial = Assert.IsType<bool>(method.Invoke(null, arguments));

        Assert.False(isSpecial);
        Assert.Equal("[日课] 锻刀完成", Assert.IsType<string>(arguments[1]));
    }
}
