using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MFAAvalonia.Helper;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public sealed class UpdatePathProtectionTests
{
    [Theory]
    [InlineData("extension", true)]
    [InlineData("EXTENSION/自定义包/a.json", true)]
    [InlineData("extension\\默认包\\配置.json", true)]
    [InlineData("assets/../extension/包", true)]
    [InlineData("extension/../assets/resource/base/extension/a.json", false)]
    [InlineData("assets/resource/base/extension/a.json", false)]
    [InlineData("extensions/a.json", false)]
    public void 仅保护规范化后的安装根扩展(string relativePath, bool expected)
    {
        Assert.Equal(expected, UpdatePathProtection.IsProtectedRelativePath(relativePath));
    }

    [Fact]
    public async Task 全量复制保留扩展修改并且不补回已移除的默认包()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var source = Path.Combine(root, "source");
            var target = Path.Combine(root, "target");
            Write(source, "extension/custom/config.json", "发行版本");
            Write(source, "extension/removed/main.json", "默认版本");
            Write(source, "assets/resource/base/extension/normal.json", "正常更新");
            Write(target, "extension/custom/config.json", "用户修改");
            var method = typeof(VersionChecker).GetMethod("CopyUpdatePayload", BindingFlags.Static | BindingFlags.NonPublic)!;
            await (Task)method.Invoke(null, new object?[] { source, null, false, CancellationToken.None,
                false, true, target, false, false, null })!;
            Assert.Equal("用户修改", File.ReadAllText(Path.Combine(target, "extension/custom/config.json")));
            Assert.False(Directory.Exists(Path.Combine(target, "extension/removed")));
            // 资源路径由另一轮复制处理，旧入口同时验证资源内同名目录不受保护。
            await VersionChecker.CopyAndDelete(source, target);
            Assert.Equal("正常更新", File.ReadAllText(Path.Combine(target, "assets/resource/base/extension/normal.json")));
            Assert.Equal("用户修改", File.ReadAllText(Path.Combine(target, "extension/custom/config.json")));
            Assert.False(Directory.Exists(Path.Combine(target, "extension/removed")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 更新事务拒绝删除或替换根扩展文件()
    {
        var extensionDirectory = Path.Combine(AppPaths.InstallRoot, "extension", "测试保护_" + Guid.NewGuid().ToString("N"));
        var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var target = Path.Combine(extensionDirectory, "config.json");
            Directory.CreateDirectory(extensionDirectory);
            File.WriteAllText(target, "用户修改");
            var source = Path.Combine(temp, "replacement.json");
            File.WriteAllText(source, "发行版本");
            var type = typeof(VersionChecker).GetNestedType("UpdateFileTransaction", BindingFlags.NonPublic)!;
            using var transaction = (IDisposable)Activator.CreateInstance(type, temp)!;
            type.GetMethod("DeleteFile")!.Invoke(transaction, new object[] { target });
            Assert.Equal("用户修改", File.ReadAllText(target));
            var relativePath = Path.GetRelativePath(AppPaths.InstallRoot, target);
            typeof(VersionChecker).GetMethod("DeleteIncrementalFiles", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new object[] { new[] { relativePath }, "Deleted", transaction });
            Assert.Equal("用户修改", File.ReadAllText(target));
            var emptyDirectory = Path.Combine(extensionDirectory, "空目录");
            Directory.CreateDirectory(emptyDirectory);
            typeof(VersionChecker).GetMethod("DeleteDirectoryForUpdate", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new object[] { Path.GetRelativePath(AppPaths.InstallRoot, emptyDirectory), emptyDirectory });
            Assert.True(Directory.Exists(emptyDirectory));
            PendingUpdateDeletionHelper.EnqueueDirectory(Path.GetRelativePath(AppPaths.InstallRoot, emptyDirectory));
            Assert.True(Directory.Exists(emptyDirectory));
            type.GetMethod("ReplaceFile")!.Invoke(transaction, new object[] { source, target, CancellationToken.None });
            Assert.Equal("用户修改", File.ReadAllText(target));
        }
        finally
        {
            Directory.Delete(extensionDirectory, true);
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public async Task 主程序直接替换保留扩展文件并且不补回默认包()
    {
        var name = "保护主程序更新_" + Guid.NewGuid().ToString("N");
        var temp = Path.Combine(Path.GetTempPath(), name);
        var source = Path.Combine(temp, "source");
        var backup = Path.Combine(temp, "backup");
        var installed = Path.Combine(AppContext.BaseDirectory, "extension", name);
        try
        {
            Write(source, "extension/" + name + "/existing.matrext", "更新包内容");
            Write(source, "extension/" + name + "/removed.matrext", "不应补回");
            Write(installed, "existing.matrext", "用户内容");
            var method = typeof(VersionChecker).GetMethod("ReplaceFilesWithRetry", BindingFlags.Static | BindingFlags.NonPublic)!;
            await (Task)method.Invoke(null, new object[] { source, backup, 1 })!;
            Assert.Equal("用户内容", File.ReadAllText(Path.Combine(installed, "existing.matrext")));
            Assert.False(File.Exists(Path.Combine(installed, "removed.matrext")));
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
            if (Directory.Exists(installed)) Directory.Delete(installed, true);
        }
    }

    private static void Write(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
