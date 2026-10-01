using System;
using System.IO;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class AppRuntimeSingleInstanceTests
{
    [Fact]
    public void 转发到既有实例被拒绝访问时必须安静退出()
    {
        var source = File.ReadAllText(FindSourcePath());

        Assert.Contains("catch (UnauthorizedAccessException)", source, StringComparison.Ordinal);
    }

    private static string FindSourcePath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var sourcePath = Path.Combine(directory.FullName, "_src", "MFAAvalonia", "AppRuntime.cs");
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
                return sourcePath;
        }

        throw new DirectoryNotFoundException("找不到 AppRuntime.cs 所在的仓库根目录。");
    }
}
