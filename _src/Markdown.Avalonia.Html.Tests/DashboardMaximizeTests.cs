using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MFAAvalonia.Views.UserControls.Dashboard;
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class DashboardMaximizeTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.StartNew(typeof(DashboardTestAppBuilder));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 设置卡片最大化还原后应保留实时视图开关与日志尺寸(bool liveViewEnabled)
    {
        await Session.Dispatch(() =>
        {
            var grid = new DashboardCardGrid { Columns = 12, Rows = 8 };
            var tasks = Card("tasks", 0, 0, 4, 8);
            var settings = Card("settings", 4, 0, 4, 8);
            var liveView = Card("live_view", 8, 0, 4, 4);
            liveView.IsVisible = liveViewEnabled;
            var log = Card("log", 8, liveViewEnabled ? 4 : 0, 4, liveViewEnabled ? 4 : 8);
            grid.Children.Add(tasks);
            grid.Children.Add(settings);
            grid.Children.Add(liveView);
            grid.Children.Add(log);
            Arrange(grid);
            var originalBounds = log.Bounds;
            var originalRow = log.GridRow;
            var originalRowSpan = log.GridRowSpan;

            for (var cycle = 0; cycle < 3; cycle++)
            {
                ToggleMaximize(settings);
                CompleteAnimation(grid);
                ToggleMaximize(settings);
                CompleteAnimation(grid);

                Assert.Equal(liveViewEnabled, liveView.IsVisible);
                Assert.True(log.IsVisible);
                Assert.Equal(originalRow, log.GridRow);
                Assert.Equal(originalRowSpan, log.GridRowSpan);
                Assert.Equal(4, log.GridColumnSpan);
                Assert.Equal(originalBounds, log.Bounds);
                Assert.False(settings.IsMaximizeTransitionActive);
            }
        }, CancellationToken.None);
    }

    private static DashboardCard Card(string id, int column, int row, int width, int height) => new()
    {
        // 容器回归只验证布局状态，使用简单模板避开主题图标资源依赖。
        Template = new FuncControlTemplate<DashboardCard>((_, _) => new Border()),
        CardId = id,
        GridColumn = column,
        GridRow = row,
        GridColumnSpan = width,
        GridRowSpan = height,
        ExpandedColumnSpan = width,
        ExpandedRowSpan = height
    };

    private static void ToggleMaximize(DashboardCard card)
        => typeof(DashboardCard).GetMethod("OnMaximizeButtonClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(card, [null, new RoutedEventArgs()]);

    private static void CompleteAnimation(DashboardCardGrid grid)
    {
        // 驱动真实的界面动画计时器，不创建窗口或访问用户布局配置。
        Arrange(grid);
        using var timeout = new CancellationTokenSource(350);
        Dispatcher.UIThread.MainLoop(timeout.Token);
        Dispatcher.UIThread.RunJobs();
        Arrange(grid);
    }

    private static void Arrange(DashboardCardGrid grid)
    {
        grid.Measure(new Size(1200, 800));
        grid.Arrange(new Rect(0, 0, 1200, 800));
    }
}

public static class DashboardTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<DashboardTestApplication>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public class DashboardTestApplication : Application
{
    public override void Initialize()
    {
        Styles.Add(new global::Avalonia.Themes.Fluent.FluentTheme());
        Styles.Add(new SukiUI.SukiTheme());
    }
}
