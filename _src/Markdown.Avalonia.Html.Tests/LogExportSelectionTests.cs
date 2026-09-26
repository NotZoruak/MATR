using MFAAvalonia.Helper;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class LogExportSelectionTests
{
    [Theory]
    [InlineData("maafw.log", true)]
    [InlineData("maafw.bak.2026.09.26-22.26.42.060.log", true)]
    [InlineData("maa.log", true)]
    [InlineData("maa.bak.20260926.log", true)]
    [InlineData("MAAFW.LOG", true)]
    [InlineData("custom.log", false)]
    [InlineData("custom.log.1", false)]
    [InlineData("log-20260926.log", false)]
    [InlineData("maafw.log.txt", false)]
    [InlineData("", false)]
    public void 命名Maa日志应包含轮转备份(string fileName, bool expected)
    {
        Assert.Equal(expected, LogExportSelection.IsMaaLogFileName(fileName));
    }

    [Fact]
    public void 导出Maa日志时应带上轮转文件并排除GUI日志与自定义日志()
    {
        var root = CreateTempRoot();
        try
        {
            var debugDir = Path.Combine(root, "debug");
            var mainLog = WriteFile(Path.Combine(debugDir, "maafw.log"));
            var rotatedLog = WriteFile(Path.Combine(debugDir, "maafw.bak.2026.09.26-22.26.42.060.log"));
            WriteFile(Path.Combine(debugDir, "logs", "log-20260926.log"));
            WriteFile(Path.Combine(debugDir, "custom.log"));
            WriteFile(Path.Combine(debugDir, "on_error", "shot.png"));

            var selected = LogExportSelection.SelectMaaLogFiles(debugDir);

            Assert.Equal(2, selected.Count);
            Assert.Contains(mainLog, selected);
            Assert.Contains(rotatedLog, selected);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 没有命名Maa日志时应回退到其它日志并排除自定义日志()
    {
        var root = CreateTempRoot();
        try
        {
            var debugDir = Path.Combine(root, "debug");
            var guiLog = WriteFile(Path.Combine(debugDir, "logs", "log-20260926.log"));
            WriteFile(Path.Combine(debugDir, "custom.log"));

            var selected = LogExportSelection.SelectMaaLogFiles(debugDir);

            Assert.Equal([guiLog], selected);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 导出GUI日志时应覆盖安装目录与历史根目录两处()
    {
        var root = CreateTempRoot();
        try
        {
            var debugLog = WriteFile(Path.Combine(root, "debug", "logs", "log-20260926.log"));
            var legacyLog = WriteFile(Path.Combine(root, "logs", "log-20260925.log"));

            var selected = LogExportSelection.SelectLogFilesInDirectories(
                Path.Combine(root, "debug", "logs"), Path.Combine(root, "logs"), Path.Combine(root, "missing"), null);

            Assert.Equal(2, selected.Count);
            Assert.Contains(debugLog, selected);
            Assert.Contains(legacyLog, selected);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 自定义日志应能在数据根目录下递归收集()
    {
        var root = CreateTempRoot();
        try
        {
            var topLevel = WriteFile(Path.Combine(root, "custom.log"));
            var nested = WriteFile(Path.Combine(root, "debug", "custom.log.1"));

            var selected = LogExportSelection.SelectCustomLogFiles(root);

            Assert.Equal(2, selected.Count);
            Assert.Contains(topLevel, selected);
            Assert.Contains(nested, selected);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 图片分类应按目录判断()
    {
        var root = CreateTempRoot();
        try
        {
            var onErrorImage = Path.Combine(root, "debug", "on_error", "shot.png");
            var visionImage = Path.Combine(root, "debug", "vision", "shot.png");
            var otherImage = Path.Combine(root, "debug", "shot.png");

            Assert.True(LogExportSelection.IsPathUnderFolder(onErrorImage, "on_error"));
            Assert.True(LogExportSelection.IsPathUnderFolder(visionImage, LogExportSelection.VisionFolder));
            Assert.False(LogExportSelection.IsPathUnderFolder(otherImage, "on_error"));
            Assert.False(LogExportSelection.IsPathUnderFolder(null, "on_error"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("shot.png", true)]
    [InlineData("shot.PNG", true)]
    [InlineData("shot.jpeg", true)]
    [InlineData("shot.log", false)]
    [InlineData("shot", false)]
    [InlineData("", false)]
    public void 图片扩展名判断应忽略大小写(string fileName, bool expected)
    {
        Assert.Equal(expected, LogExportSelection.IsImageFile(fileName));
    }

    private static string WriteFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "matr-log-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
