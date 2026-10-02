using System;
using System.Reflection;
using MFAAvalonia;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class StartupLaunchTargetResolverTests
{
    [Fact]
    public void 指定实例时必须使用解析出的目标实例()
    {
        var result = Resolve("daily-id", selector => selector == "daily-id" ? "daily-id" : null, "default");

        Assert.Equal("daily-id", result);
    }

    [Fact]
    public void 指定实例无法解析时不得回退到当前实例()
    {
        var result = Resolve("missing-id", _ => null, "default");

        Assert.Null(result);
    }

    private static string? Resolve(string? requestedInstance, Func<string, string?> resolveInstanceId, string currentInstanceId)
    {
        var method = typeof(AppRuntime).GetMethod(
            "ResolveStartupInstance",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        return (string?)method!.Invoke(null, [requestedInstance, resolveInstanceId, currentInstanceId]);
    }
}
