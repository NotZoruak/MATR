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
using MATR.GameLogReaderSdk;
using MFAAvalonia.Services;
using MFAAvalonia.Views.Pages;
using Newtonsoft.Json.Linq;
using Xunit;
using MATR.GameLogReader;
using MATR.GameLogReaderSdk;

namespace Markdown.Avalonia.Html.Tests;

public sealed class ExtensionUiTests
{
    internal static readonly HeadlessUnitTestSession Session = HeadlessUnitTestSession.StartNew(typeof(ExtensionTestAppBuilder));

    [Fact]
    public async Task 部队生存颜色随伤势和主题更新()
    {
        await Session.Dispatch(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "MATR-team-health-" + Guid.NewGuid().ToString("N"));
            var view = new TeamStatusView(directory);
            var snapshot = new GameOverviewSnapshot { HasFullSwordPool = true };
            foreach (var (serial, hp) in new[] { ("1", 30), ("2", 65), ("3", 100) })
            {
                snapshot.HeldSwords[serial] = JObject.FromObject(new { sword_id = 3, hp, hp_max = 100 });
                snapshot.SwordTeams[serial] = "1";
                snapshot.SwordTeamPositions[serial] = serial;
            }
            var window = new Window { Width = 1400, Height = 700, Content = view };
            try
            {
                view.SetSnapshot(snapshot);
                window.Show();
                foreach (var (theme, critical, warning) in new[]
                {
                    (global::Avalonia.Styling.ThemeVariant.Light, "#C62828", "#C77700"),
                    (global::Avalonia.Styling.ThemeVariant.Dark, "#EF5350", "#FFB74D")
                })
                {
                    window.RequestedThemeVariant = theme;
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    var texts = view.FindControl<DataGrid>("TeamGrid")!.GetVisualDescendants().OfType<TextBlock>().ToArray();
                    Assert.Equal(Color.Parse(critical), Assert.IsAssignableFrom<ISolidColorBrush>(texts.Single(x => x.Text == "30/100").Foreground).Color);
                    Assert.Equal(Color.Parse(warning), Assert.IsAssignableFrom<ISolidColorBrush>(texts.Single(x => x.Text == "65/100").Foreground).Color);
                }
                snapshot.HeldSwords["1"]["hp"] = 100;
                snapshot.HeldSwords["2"]["hp"] = 30;
                view.SetSnapshot(snapshot);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var updated = view.FindControl<DataGrid>("TeamGrid")!.GetVisualDescendants().OfType<TextBlock>().ToArray();
                Assert.Equal(Color.Parse("#EF5350"), Assert.IsAssignableFrom<ISolidColorBrush>(updated.Single(x => x.Text == "30/100").Foreground).Color);
                Assert.All(updated.Where(x => x.Text == "100/100"), text => Assert.NotEqual(Color.Parse("#EF5350"), Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task 状态总览内番数据布局有效且显示倒计时()
    {
        await Session.Dispatch(() =>
        {
            var view = new DefaultOverviewView(() => Task.CompletedTask);
            var snapshot = new GameOverviewSnapshot();
            snapshot.DutyMembers["horse"] = ["3", "3"];
            snapshot.DutyMemberSerials["horse"] = ["101", "102"];
            snapshot.DutyMembers["field"] = ["3", "3"];
            snapshot.DutyMemberSerials["field"] = ["101", "102"];
            snapshot.DutyMembers["bout"] = [null, null];
            foreach (var serial in new[] { "101", "102" })
                snapshot.HeldSwords[serial] = JObject.Parse("{\"sword_id\":3,\"def\":56,\"def_up\":10,\"mobile\":74,\"mobile_up\":0,\"hp_max\":94,\"hp_up\":2,\"scout\":28,\"scout_up\":1}");
            var now = new DateTimeOffset(2026, 11, 23, 14, 0, 0, TimeSpan.FromHours(8));
            snapshot.WorkItems["Duty"] = [new GameOverviewWorkEntry("", "2026-11-24 00:43:26")];
            view.Update(snapshot, "测试数据");
            view.Tick(now);
            foreach (var name in new[] { "AccountCardBody", "ResourcesCardBody", "TeamsCardBody" })
                view.FindControl<StackPanel>(name)!.IsVisible = false;
            var window = new Window { Width = 760, Height = 1100, Content = view };
            try
            {
                window.Show();
                foreach (var width in new[] { 620, 760, 900 })
                {
                    window.Width = width;
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(2, view.FindControl<global::Avalonia.Controls.Primitives.UniformGrid>("DutyJobs")!.Columns);
                    var horse = view.FindControl<StackPanel>("DutyHorseMembers")!;
                    foreach (var card in horse.Children.OfType<Border>())
                    foreach (var text in card.GetVisualDescendants().OfType<TextBlock>())
                    {
                        var point = text.TranslatePoint(default, card)!.Value;
                        Assert.True(point.X >= 0 && point.X + text.Bounds.Width <= card.Bounds.Width + 1);
                    }
                    var texts = horse.GetVisualDescendants().OfType<TextBlock>().Select(x => x.Text).ToArray();
                    Assert.Contains("0/0", texts);
                    Assert.Contains("4/14", texts);
                    Assert.DoesNotContain("74/74", texts);
                    Assert.Contains("MAX", texts);
                    Assert.Contains("防御", texts);
                    Assert.Contains("机动", texts);
                    Assert.DoesNotContain("侦察", texts);
                    var field = view.FindControl<StackPanel>("DutyFieldMembers")!;
                    var progress = field.GetVisualDescendants().OfType<Grid>().First(grid => grid.Height == 3);
                    Assert.Equal(5d / 7, progress.ColumnDefinitions[0].Width.Value, 6);
                    Assert.Empty(view.FindControl<StackPanel>("DutyBoutMembers")!.Children);
                    Assert.False(view.FindControl<Border>("DutyBoutCard")!.IsVisible);
                }
                var schedule = GameOverviewWorkStatus.FormatSchedule("2026-11-24 00:43:26", now);
                Assert.Equal("结束：11-24 00:43:26，剩余：10:43:26", schedule.Text);
            }
            finally { window.Close(); }
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
