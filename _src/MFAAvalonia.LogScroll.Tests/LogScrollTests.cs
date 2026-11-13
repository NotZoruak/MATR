using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MFAAvalonia.Extensions;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(MFAAvalonia.LogScroll.Tests.TestApp))]
namespace MFAAvalonia.LogScroll.Tests;

public class TestApp : Application
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
    public override void Initialize() => Styles.Add(new FluentTheme());
}

public class LogScrollTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestoreFocusPreservesPositionWithFix(bool applyFix)
    {
        var nested = new Markdown.Avalonia.Full.MarkdownScrollViewer
        {
            Markdown = "<details><summary>旧日志</summary>日志详情</details>"
        };
        var logs = new StackPanel();
        logs.Children.Add(nested);
        var scroll = new ScrollViewer { Content = logs, BringIntoViewOnFocusChange = false };
        ScrollViewerExtensions.SetAutoScroll(scroll, true);
        if (applyFix)
            scroll.Styles.Add(new StyleInclude(new Uri("avares://MFAAvalonia.LogScroll.Tests/"))
            {
                Source = new Uri("avares://MFAAvalonia.LogScroll.Tests/LogScrollStyles.axaml")
            });
        var outside = new Button { Content = "日志之外" };
        var layout = new DockPanel();
        DockPanel.SetDock(outside, Dock.Top);
        layout.Children.Add(outside);
        layout.Children.Add(scroll);
        var window = new Window { Width = 500, Height = 300, Content = layout };
        window.Show();
        try
        {
            Flush(window);
            var expander = nested.GetVisualDescendants().OfType<Expander>().Single();
            var oldLog = expander.GetVisualDescendants().OfType<ToggleButton>().Single();
            Assert.True(oldLog.Focus());
            Assert.True(outside.Focus());
            for (var i = 0; i < 30; i++)
                logs.Children.Add(new TextBlock { Text = $"新日志 {i}", Height = 40 });
            Flush(window);
            Assert.True(scroll.Offset.Y > 500);
            var bottom = scroll.Offset;
            Assert.True(oldLog.Focus());
            Flush(window);
            if (!applyFix)
            {
                // 仅关闭外层焦点定位仍会跳回旧日志。
                Assert.True(scroll.Offset.Y < bottom.Y - 500);
                return;
            }
            Assert.Equal(bottom, scroll.Offset);
            // 手动翻阅暂停跟随，重新聚焦也保持当前位置。
            scroll.Offset = new Vector(0, 200);
            Flush(window);
            logs.Children.Add(new TextBlock { Text = "翻阅期间新增", Height = 40 });
            Flush(window);
            Assert.Equal(200, scroll.Offset.Y);
            Assert.True(outside.Focus());
            Assert.True(oldLog.Focus());
            Flush(window);
            Assert.Equal(200, scroll.Offset.Y);
            scroll.ScrollToEnd();
            Flush(window);
            logs.Children.Add(new TextBlock { Text = "恢复跟随", Height = 40 });
            Flush(window);
            Assert.Equal(scroll.ScrollBarMaximum.Y, scroll.Offset.Y);
            // 禁止焦点定位不影响折叠功能。
            oldLog.IsChecked = true;
            Flush(window);
            Assert.True(expander.IsExpanded);
            oldLog.IsChecked = false;
            Flush(window);
            Assert.False(expander.IsExpanded);
        }
        finally { window.Close(); }
    }

    private static void Flush(Window window)
    {
        for (var i = 0; i < 3; i++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
