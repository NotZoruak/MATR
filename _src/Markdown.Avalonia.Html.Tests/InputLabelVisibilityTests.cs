using MFAAvalonia.Helper;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class InputLabelVisibilityTests
{
    [Fact]
    public void 单字段无说明时标题与输入框同列_有说明时显示独立标题行()
    {
        Assert.False(TaskOptionGenerator.ShouldShowInputOptionHeader(1, false, false));
        Assert.True(TaskOptionGenerator.ShouldShowInputOptionHeader(1, true, false));
        Assert.True(TaskOptionGenerator.ShouldShowInputOptionHeader(2, false, false));
        Assert.False(TaskOptionGenerator.ShouldShowInputOptionHeader(1, false, true));
    }
}
