using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MATR.ExtensionSdk;
using MFAAvalonia.Services;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public sealed class TeamStatusPackageUiTests
{
    [Fact]
    public async Task 独立部队包首次加载时能够解析伤势颜色资源()
    {
        await ExtensionUiTests.Session.Dispatch(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "MATR-team-package-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            using (var archive = System.IO.Compression.ZipFile.Open(Path.Combine(directory, "team-status.matrext"), System.IO.Compression.ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open()))
                    writer.Write("{\"id\":\"matr.team-status\",\"name\":\"部队状况\",\"version\":\"1\",\"apiVersion\":1,\"entryAssembly\":\"MATR.DefaultExtensions.dll\",\"entryType\":\"MATR.DefaultExtensions.TeamsWorkspace\"}");
                // 通过文件读取程序集，避免提前加载默认上下文而掩盖独立包的资源解析问题。
                using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "MATR.DefaultExtensions.dll"));
                using var output = archive.CreateEntry("MATR.DefaultExtensions.dll").Open();
                input.CopyTo(output);
            }
            var package = Assert.Single(ExtensionPackageLoader.Scan(directory).Packages);
            using var loaded = ExtensionPackageLoader.Load(package, Path.Combine(directory, "cache"));
            var page = loaded.Workspace.CreateView(new PackageTestContext(directory));
            loaded.Workspace.Update(new ExtensionState("{\"HasFullSwordPool\":true,\"HeldSwords\":{\"1\":{\"sword_id\":3,\"hp\":30,\"hp_max\":100}},\"SwordTeams\":{\"1\":\"1\"},\"SwordTeamPositions\":{\"1\":\"1\"}}", "伤势测试"));
            var window = new Window { Width = 1400, Height = 700, Content = page, RequestedThemeVariant = global::Avalonia.Styling.ThemeVariant.Light };
            try
            {
                window.Show();
                ((Button)((Control)page).FindControl<Grid>("TeamCards")!.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var text = ((Control)page).GetVisualDescendants().OfType<TextBlock>().Single(x => x.Text == "30/100");
                Assert.Equal(Color.Parse("#C62828"), Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
}
