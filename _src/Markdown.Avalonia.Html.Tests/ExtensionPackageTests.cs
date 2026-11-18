using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using MFAAvalonia.Services;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public sealed class ExtensionPackageTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MATR-extensions-" + Guid.NewGuid().ToString("N"));
    public ExtensionPackageTests() => Directory.CreateDirectory(_directory);
    private void Package(string file, string id = "example", int apiVersion = 1, string? extra = null)
    {
        using var zip = ZipFile.Open(Path.Combine(_directory, file), ZipArchiveMode.Create);
        using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open()))
            writer.Write(JsonSerializer.Serialize(new { id, name = "测试扩展", version = "1.0.0", apiVersion, entryAssembly = "Example.dll", entryType = typeof(PackageTestWorkspace).FullName, order = 10, icon = "List" }));
        zip.CreateEntryFromFile(typeof(PackageTestWorkspace).Assembly.Location, "Example.dll");
        if (extra != null) using (var writer = new StreamWriter(zip.CreateEntry(extra).Open())) writer.Write("非法路径");
    }
    [Fact] public void 空目录不产生入口() => Assert.Empty(ExtensionPackageLoader.Scan(_directory).Packages);
    [Fact] public void 合法包只扫描清单而不执行代码()
    {
        Package("example.matrext");
        var result = ExtensionPackageLoader.Scan(_directory);
        Assert.Single(result.Packages);
        Assert.Empty(result.Errors);
    }
    [Fact] public void 移除包后扫描不再显示入口()
    {
        Package("example.matrext");
        Assert.Single(ExtensionPackageLoader.Scan(_directory).Packages);
        File.Delete(Path.Combine(_directory, "example.matrext"));
        Assert.Empty(ExtensionPackageLoader.Scan(_directory).Packages);
    }
    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("/absolute.txt")]
    [InlineData("C:/absolute.txt")]
    [InlineData("folder/../../outside.txt")]
    public void 拒绝包内逃逸路径(string path)
    {
        Package("bad.matrext", extra: path);
        var result = ExtensionPackageLoader.Scan(_directory);
        Assert.Empty(result.Packages);
        Assert.Single(result.Errors);
    }
    [Fact] public void 坏包不妨碍其他包()
    {
        File.WriteAllText(Path.Combine(_directory, "bad.matrext"), "损坏的压缩包");
        Package("good.matrext");
        var result = ExtensionPackageLoader.Scan(_directory);
        Assert.Single(result.Packages);
        Assert.Single(result.Errors);
    }
    [Fact] public void 重复标识的包全部拒绝()
    {
        Package("one.matrext"); Package("two.matrext");
        Assert.Empty(ExtensionPackageLoader.Scan(_directory).Packages);
    }
    [Fact] public void 伪造的程序集不能仅凭清单成为有效扩展()
    {
        Package("fake.matrext");
        using (var archive = ZipFile.Open(Path.Combine(_directory, "fake.matrext"), ZipArchiveMode.Update))
        {
            archive.GetEntry("Example.dll")!.Delete();
            using var writer = new StreamWriter(archive.CreateEntry("Example.dll").Open());
            writer.Write("这不是托管程序集");
        }
        var result = ExtensionPackageLoader.Scan(_directory);
        Assert.Empty(result.Packages);
        Assert.Single(result.Errors);
    }
    [Fact] public void 不兼容接口版本拒绝()
    {
        Package("future.matrext", apiVersion: 2);
        Assert.Empty(ExtensionPackageLoader.Scan(_directory).Packages);
    }
    private void LoadablePackage(string file, string id)
    {
        using var zip = ZipFile.Open(Path.Combine(_directory, file), ZipArchiveMode.Create);
        using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open()))
            writer.Write(JsonSerializer.Serialize(new { id, name = "独立测试页", version = "1.0.0", apiVersion = 1, entryAssembly = "Example.dll", entryType = typeof(PackageTestWorkspace).FullName }));
        zip.CreateEntryFromFile(typeof(PackageTestWorkspace).Assembly.Location, "Example.dll");
    }
    [Fact] public async System.Threading.Tasks.Task 独立包可以通过公共接口创建新页面且加载上下文隔离()
    {
        LoadablePackage("first.matrext", "first");
        LoadablePackage("second.matrext", "second");
        var packages = ExtensionPackageLoader.Scan(_directory).Packages;
        await ExtensionUiTests.Session.Dispatch(() =>
        {
            using var first = ExtensionPackageLoader.Load(packages[0], Path.Combine(_directory, "cache"));
            using var second = ExtensionPackageLoader.Load(packages[1], Path.Combine(_directory, "cache"));
            Assert.NotSame(first.Workspace.GetType().Assembly, second.Workspace.GetType().Assembly);
            var page = first.Workspace.CreateView(new PackageTestContext(_directory));
            Assert.IsType<global::Avalonia.Controls.TextBlock>(page);
            first.Workspace.Update(new MATR.ExtensionSdk.ExtensionState(null, "测试状态"));
            Assert.Equal("测试状态", ((global::Avalonia.Controls.TextBlock)page).Text);
        }, System.Threading.CancellationToken.None);
    }
    [Fact] public void 扫描之后替换的包不能加载()
    {
        LoadablePackage("example.matrext", "example");
        var package = Assert.Single(ExtensionPackageLoader.Scan(_directory).Packages);
        File.WriteAllText(package.Path, "已经替换");
        Assert.Throws<InvalidDataException>(() => ExtensionPackageLoader.Load(package, Path.Combine(_directory, "cache")));
    }
    public void Dispose()
    {
        // 已加载程序集会被运行时暂时占用，测试缓存由系统临时目录清理。
        try { Directory.Delete(_directory, true); }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }
}

public sealed class PackageTestWorkspace : MATR.ExtensionSdk.IExtensionWorkspace
{
    private readonly global::Avalonia.Controls.TextBlock _view = new();
    public global::Avalonia.Controls.Control CreateView(MATR.ExtensionSdk.IExtensionContext context) => _view;
    public void Update(MATR.ExtensionSdk.ExtensionState state) => _view.Text = state.Status;
    public void Tick(DateTimeOffset now) { }
    public void Dispose() { }
}
public sealed record PackageTestContext(string DataDirectory) : MATR.ExtensionSdk.IExtensionContext
{
    public string PackageDirectory => DataDirectory;
    public System.Threading.Tasks.Task SyncWarehouseAsync() => System.Threading.Tasks.Task.CompletedTask;
}
