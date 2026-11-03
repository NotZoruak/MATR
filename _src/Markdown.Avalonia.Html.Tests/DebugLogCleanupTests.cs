using MFAAvalonia.Helper;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public sealed class DebugLogCleanupTests : IDisposable
{
    private readonly string _testRoot = Path.Combine(Path.GetTempPath(), "MATR-DebugLogCleanupTests");
    private readonly string _debugDirectory;

    public DebugLogCleanupTests()
    {
        _debugDirectory = Path.Combine(_testRoot, Guid.NewGuid().ToString("N"), "debug");
        Directory.CreateDirectory(_debugDirectory);
    }

    [Fact]
    public void 导出清理应保留正在写入的主日志并清除过期备份和截图()
    {
        var mainLog = WriteFile("maafw.log", "正在写入的主日志");
        var oldLog = WriteFile("maafw.bak.old.log");
        var oldImage = WriteFile(Path.Combine("on_error", "old.png"));
        var recentLog = WriteFile("maafw.bak.recent.log");
        var recentImage = WriteFile(Path.Combine("on_error", "recent.png"));
        var guiLog = WriteFile(Path.Combine("logs", "log-old.log"));
        foreach (var file in new[] {mainLog, oldLog, oldImage, guiLog})
            File.SetLastWriteTime(file, DateTime.Now.AddDays(-4));
        var mainWriteTime = File.GetLastWriteTimeUtc(mainLog);

        using var writer = new FileStream(mainLog, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
        AppPaths.CleanupOldDebugLogs(_debugDirectory, rotateMainLog: false);

        Assert.True(File.Exists(mainLog));
        using var reader = new StreamReader(new FileStream(mainLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        Assert.Equal("正在写入的主日志", reader.ReadToEnd());
        Assert.Equal(mainWriteTime, File.GetLastWriteTimeUtc(mainLog));
        Assert.False(File.Exists(oldLog));
        Assert.False(File.Exists(oldImage));
        Assert.True(File.Exists(recentLog));
        Assert.True(File.Exists(recentImage));
        Assert.True(File.Exists(guiLog));
        Assert.Single(Directory.GetFiles(_debugDirectory, "maafw.bak.*.log"));
    }

    [Fact]
    public void 启动清理默认仍应轮转主日志()
    {
        var mainLog = WriteFile("maafw.log", "上一轮运行日志");

        AppPaths.CleanupOldDebugLogs(_debugDirectory);

        Assert.False(File.Exists(mainLog));
        var rotatedLog = Assert.Single(Directory.GetFiles(_debugDirectory, "maafw.bak.*.log"));
        Assert.Equal("上一轮运行日志", File.ReadAllText(rotatedLog));
    }

    [Theory]
    [InlineData(false, 12, 55)]
    [InlineData(true, 10, 50)]
    public void 导出容量清理应仅在总大小超过五百MiB时保留最新文件(bool exceedLimit, int expectedLogCount, int expectedImageCount)
    {
        var mainLog = WriteFile("maafw.log", "当前主日志");
        for (var index = 0; index < 12; index++)
            WriteFile($"maafw.bak.{index:D3}.log");
        for (var index = 0; index < 55; index++)
            WriteFile(Path.Combine("on_error", $"{index:D3}.png"));

        // 使用定长占位文件触发实际容量判断，不生成庞大的日志文本。
        var payloadSize = Directory.GetFiles(_debugDirectory, "*", SearchOption.AllDirectories)
            .Sum(file => new FileInfo(file).Length);
        using (var padding = File.Create(Path.Combine(_debugDirectory, "padding.bin")))
            padding.SetLength(500L * 1024 * 1024 - payloadSize + (exceedLimit ? 1 : 0));

        AppPaths.CleanupOldDebugLogs(_debugDirectory, rotateMainLog: false);

        Assert.Equal(expectedLogCount, Directory.GetFiles(_debugDirectory, "maafw.bak.*.log").Length);
        Assert.Equal(expectedImageCount, Directory.GetFiles(Path.Combine(_debugDirectory, "on_error"), "*.png").Length);
        Assert.Equal("当前主日志", File.ReadAllText(mainLog));
        Assert.True(File.Exists(Path.Combine(_debugDirectory, "maafw.bak.011.log")));
        Assert.True(File.Exists(Path.Combine(_debugDirectory, "on_error", "054.png")));
        Assert.Equal(!exceedLimit, File.Exists(Path.Combine(_debugDirectory, "maafw.bak.000.log")));
        Assert.Equal(!exceedLimit, File.Exists(Path.Combine(_debugDirectory, "on_error", "000.png")));
    }

    private string WriteFile(string relativePath, string content = "测试日志")
    {
        var path = Path.Combine(_debugDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        var target = Path.GetFullPath(Path.GetDirectoryName(_debugDirectory)!);
        var prefix = Path.GetFullPath(_testRoot) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("测试清理目录超出预期范围");
        Directory.Delete(target, true);
    }
}
