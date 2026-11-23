using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MATR.ExtensionSdk;
using MFAAvalonia.Services;
using MFAAvalonia.Views.Pages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public sealed class OverviewDutyLayoutTests
{
    [Fact]
    public async Task 内番属性不越界且时间显示剩余在最后()
    {
        await ExtensionUiTests.Session.Dispatch(() =>
        {
            var view = new DefaultOverviewView(new PackageTestContext(Path.GetTempPath()));
            var snapshot = new GameOverviewLogParser.Snapshot();
            snapshot.DutyMembers["horse"] = ["3", "3"];
            snapshot.DutyMemberSerials["horse"] = ["101", "102"];
            snapshot.DutyMembers["field"] = ["3", "3"];
            snapshot.DutyMemberSerials["field"] = ["101", "102"];
            snapshot.DutyMembers["bout"] = [null, null];
            foreach (var serial in new[] { "101", "102" })
                snapshot.HeldSwords[serial] = JObject.Parse("{\"sword_id\":3,\"def\":56,\"def_up\":10,\"mobile\":74,\"mobile_up\":0,\"hp_max\":94,\"hp_up\":2,\"scout\":28,\"scout_up\":1}");
            var now = new DateTimeOffset(2026, 11, 23, 14, 0, 0, TimeSpan.FromHours(8));
            snapshot.WorkItems["Duty"] = [new GameOverviewLogParser.WorkEntry("", "2026-11-24 00:43:26")];
            view.Update(new ExtensionState(JsonConvert.SerializeObject(snapshot), "测试数据"));
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
                    var fieldTexts = field.GetVisualDescendants().OfType<TextBlock>().Select(x => x.Text).ToArray();
                    Assert.Contains("5/7", fieldTexts);
                    Assert.Contains("2/3", fieldTexts);
                    var progress = field.GetVisualDescendants().OfType<Grid>()
                        .First(grid => grid.Height == 3);
                    Assert.Equal(5d / 7, progress.ColumnDefinitions[0].Width.Value, 6);
                    var unassigned = view.FindControl<StackPanel>("DutyBoutMembers")!;
                    Assert.Empty(unassigned.Children);
                    Assert.False(view.FindControl<Border>("DutyBoutCard")!.IsVisible);
                    foreach (var stats in horse.GetVisualDescendants().OfType<global::Avalonia.Controls.Primitives.UniformGrid>())
                        Assert.Equal(width == 620 ? 1 : 2, stats.Columns);
                }
                var schedule = GameOverviewWorkStatus.FormatSchedule("2026-11-24 00:43:26", now);
                Assert.Equal("结束：11-24 00:43:26，剩余：10:43:26", schedule.Text);
                var completed = GameOverviewWorkStatus.FormatSchedule("2026-11-23 13:59:59", now);
                Assert.True(completed.Completed);
                Assert.Equal("已完成", completed.Text);
                var capturePath = Path.Combine(Path.GetTempPath(), "MATR-overview-duty-layout.png");
                window.CaptureRenderedFrame()!.Save(capturePath);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
}
