using MFAAvalonia.Extensions.MaaFW.Custom;
using System;
using System.Collections.Generic;
using System.Reflection;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class ListOcrScanGestureTests
{
    [Fact]
    public void 列表上滑的L形收尾应向右移动()
    {
        var method = typeof(ListOcrScan).GetMethod(
            "BuildScrollPath",
            BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(method);

        var path = Assert.IsAssignableFrom<IReadOnlyList<(int X, int Y)>>(method!.Invoke(null, new object?[] { new[] { 864, 534, 864, 187 } }));

        Assert.Equal((864, 187), path[13]);
        Assert.Equal((1064, 187), path[^1]);
    }
}
