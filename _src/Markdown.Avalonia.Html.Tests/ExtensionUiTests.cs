using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MATR.DefaultExtensions;
using MATR.ExtensionSdk;
using MFAAvalonia.Services;
using MFAAvalonia.Views.Pages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public sealed class ExtensionUiTests
{
    internal static readonly HeadlessUnitTestSession Session = HeadlessUnitTestSession.StartNew(typeof(ExtensionTestAppBuilder));

    [Fact]
    public async Task 默认独立页面能够真实挂载且保护表头紧凑完整与满值金色生效()
    {
        await Session.Dispatch(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "MATR-ui-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var context = new PackageTestContext(directory);
            using var overview = new OverviewWorkspace();
            using var swords = new SwordsWorkspace();
            var snapshot = new GameOverviewLogParser.Snapshot { HasFullSwordPool = true, Account = "测试账号" };
            snapshot.HeldSwords["1"] = JObject.Parse("{\"sword_id\":3,\"level\":99,\"exp\":1000,\"atk\":89,\"atk_up\":0,\"hp\":40,\"hp_max\":40,\"hp_up\":1,\"protect\":1}");
            var state = new ExtensionState(JsonConvert.SerializeObject(snapshot), "测试状态");
            var window = new Window { Width = 1400, Height = 700 };
            try
            {
                window.Content = overview.CreateView(context);
                overview.Update(state);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                overview.Tick(DateTimeOffset.UtcNow);
                window.Content = swords.CreateView(context);
                swords.Update(state);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var grid = ((Control)window.Content).GetVisualDescendants().OfType<DataGrid>().Single();
                Assert.Equal(72, grid.Columns[0].ActualWidth);
                var header = grid.GetVisualDescendants().OfType<DataGridColumnHeader>().First(x => x.Content?.ToString() == "保护 ▾");
                Assert.False(header.GetVisualDescendants().OfType<global::Avalonia.Controls.Shapes.Path>().Single(x => x.Name == "SortIcon").IsVisible);
                var cell = grid.GetVisualDescendants().OfType<Border>().First(x => Equals(x.Tag, "atk"));
                Assert.Equal(Color.Parse("#F4E4AB"), Assert.IsAssignableFrom<ISolidColorBrush>(cell.Background).Color);
                var view = Assert.IsType<HeldSwordsView>(window.Content);
                var menu = (ContextMenu)typeof(HeldSwordsView).GetMethod("ColumnsMenu", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(view, null)!;
                var ranbu = menu.Items.OfType<MenuItem>().Single(x => x.Header?.ToString() == "乱舞");
                // 模拟模板复选框的双向绑定，不依赖 MenuItem.Click 事件。
                ranbu.IsChecked = false;
                Assert.False(grid.Columns.Single(x => x.SortMemberPath == "Ranbu").IsVisible);
                ranbu.IsChecked = true;
                Assert.True(grid.Columns.Single(x => x.SortMemberPath == "Ranbu").IsVisible);
            }
            finally { window.Close(); Directory.Delete(directory, true); }
        }, CancellationToken.None);
    }
    [Fact]
    public async Task 真正默认包在独立加载上下文内能够挂载编译界面并读取内嵌资料()
    {
        await Session.Dispatch(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "MATR-default-package-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            foreach (var entry in new[] { "OverviewWorkspace", "SwordsWorkspace" })
            {
                using (var archive = System.IO.Compression.ZipFile.Open(Path.Combine(directory, entry + ".matrext"), System.IO.Compression.ZipArchiveMode.Create))
                {
                    using (var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open()))
                        writer.Write(System.Text.Json.JsonSerializer.Serialize(new { id = entry, name = entry, version = "1", apiVersion = 1, entryAssembly = "MATR.DefaultExtensions.dll", entryType = "MATR.DefaultExtensions." + entry }));
                    using var input = File.OpenRead(typeof(OverviewWorkspace).Assembly.Location);
                    using var output = archive.CreateEntry("MATR.DefaultExtensions.dll").Open();
                    input.CopyTo(output);
                }
            }
            var window = new Window { Width = 1200, Height = 650 };
            try
            {
                window.Show();
                foreach (var package in ExtensionPackageLoader.Scan(directory).Packages)
                {
                    using var loaded = ExtensionPackageLoader.Load(package, Path.Combine(directory, "cache"));
                    var page = loaded.Workspace.CreateView(new PackageTestContext(directory));
                    loaded.Workspace.Update(new ExtensionState("{\"Account\":\"测试\",\"HasFullSwordPool\":true,\"HeldSwords\":{\"1\":{\"sword_id\":3,\"level\":1,\"exp\":0}}}", "测试包加载"));
                    window.Content = page;
                    Dispatcher.UIThread.RunJobs();
                    loaded.Workspace.Tick(DateTimeOffset.UtcNow);
                    Assert.True(page.IsAttachedToVisualTree());
                    window.Content = null;
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("attach")]
    [InlineData("update")]
    [InlineData("tick")]
    public async Task 单个扩展创建后挂载或回调失败不妨碍另一个工作区(string mode)
    {
        await Session.Dispatch(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "MATR-fault-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            void Package(string id, Type entry)
            {
                using var archive = System.IO.Compression.ZipFile.Open(Path.Combine(directory, id + ".matrext"), System.IO.Compression.ZipArchiveMode.Create);
                using (var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open()))
                    writer.Write(System.Text.Json.JsonSerializer.Serialize(new { id, name = id, version = "1", apiVersion = 1, entryAssembly = "Example.dll", entryType = entry.FullName }));
                using var input = File.OpenRead(entry.Assembly.Location);
                using var output = archive.CreateEntry("Example.dll").Open();
                input.CopyTo(output);
            }
            Package("bad-" + mode, typeof(PackageFaultWorkspace));
            Package("good", typeof(PackageTestWorkspace));
            var catalog = ExtensionPackageLoader.Scan(directory);
            Assert.Equal(2, catalog.Packages.Count);
            var host = new ExtensionOverviewView(catalog, directory);
            var window = new Window { Width = 1000, Height = 700, Content = host };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                typeof(ExtensionOverviewView).GetMethod("SelectWorkspace", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, new object[] { catalog.Packages.Single(x => x.Manifest.Id == "good") });
                typeof(ExtensionOverviewView).GetMethod("TickWorkspaces", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, null);
                typeof(ExtensionOverviewView).GetMethod("PublishState", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, new object[] { "另一个页面仍正常" });
                var content = host.FindControl<Grid>("WorkspaceContent")!.Children.Single();
                Assert.Equal("另一个页面仍正常", Assert.IsType<TextBlock>(content).Text);
            }
            finally
            {
                window.Close();
                typeof(ExtensionOverviewView).GetMethod("ReleaseWorkspaces", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, null);
            }
        }, CancellationToken.None);
    }

}

public sealed class ExtensionTestApplication : Application
{
    public override void Initialize()
    {
        Styles.Add(new global::Avalonia.Themes.Fluent.FluentTheme());
        Styles.Add(new SukiUI.SukiTheme());
    }
}
public static class ExtensionTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<ExtensionTestApplication>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia().WithInterFont();
}

public sealed class PackageFaultWorkspace : IExtensionWorkspace
{
    private string _mode = "";
    public Control CreateView(IExtensionContext context)
    {
        _mode = Path.GetFileName(context.DataDirectory);
        return _mode.EndsWith("attach", StringComparison.Ordinal) ? new FaultyAttachControl() : new TextBlock();
    }
    public void Update(ExtensionState state) { if (_mode.EndsWith("update", StringComparison.Ordinal)) throw new InvalidOperationException("测试更新失败"); }
    public void Tick(DateTimeOffset now) { if (_mode.EndsWith("tick", StringComparison.Ordinal)) throw new InvalidOperationException("测试刷新失败"); }
    public void Dispose() { }
}
public sealed class FaultyAttachControl : Border
{
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        throw new InvalidOperationException("测试首次挂载失败");
    }
}
