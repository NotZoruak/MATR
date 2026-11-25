using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using MFAAvalonia.Services;
using MFAAvalonia.Views.Pages;
using Xunit;
using MATR.GameLogReader;
using MATR.GameLogReaderSdk;

namespace Markdown.Avalonia.Html.Tests;

public sealed class TeamStatusWrappingTests
{
    [Fact]
    public async Task 长活动名在卡片内换行且标题状态保持可见()
    {
        await ExtensionUiTests.Session.Dispatch(() =>
        {
            var view = new TeamStatusView(Path.GetTempPath());
            view.Tick(new DateTimeOffset(2026, 11, 23, 14, 0, 0, TimeSpan.FromHours(8)));
            var snapshot = new GameOverviewSnapshot();
            snapshot.Teams["1"] = new GameOverviewTeamEntry("3", null, SortieMap: "活动 18505-1");
            snapshot.Teams["2"] = new GameOverviewTeamEntry("2", "2026-11-23 16:00:00", "2-1");
            snapshot.Teams["3"] = new GameOverviewTeamEntry("2", "2026-11-23 13:00:00", "4-2");
            view.SetSnapshot(snapshot);
            var window = new Window { Width = 680, Height = 500, Content = view };
            try
            {
                window.Show();
                foreach (var width in new[] { 680, 1000, 680 })
                {
                    window.Width = width;
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    view.Measure(new Size(width, 500));
                    view.Arrange(new Rect(0, 0, width, 500));
                    view.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    foreach (var item in view.FindControl<Grid>("TeamCards")!.Children)
                    {
                        var location = item.TranslatePoint(default, view)!.Value;
                        Assert.True(location.X >= 0 && location.X + item.Bounds.Width <= width + 1);
                        var button = Assert.IsType<Button>(item);
                        var details = Assert.IsType<StackPanel>(button.Content);
                        foreach (var text in details.Children.OfType<TextBlock>())
                        {
                            var position = text.TranslatePoint(default, button)!.Value;
                            Assert.True(System.Math.Abs(position.X + text.Bounds.Width / 2 - button.Bounds.Width / 2) <= 1,
                                $"文字：{text.Text}，中心：{position.X + text.Bounds.Width / 2}，卡片中心：{button.Bounds.Width / 2}");
                        }
                    }
                    var card = Assert.IsType<Button>(view.FindControl<Grid>("TeamCards")!.Children[0]);
                    var content = Assert.IsType<StackPanel>(card.Content);
                    var texts = content.Children.OfType<TextBlock>().ToArray();
                    foreach (var text in texts)
                    {
                        var point = text.TranslatePoint(default, card)!.Value;
                        Assert.True(point.X >= 0 && point.X + text.Bounds.Width <= card.Bounds.Width + 0.5,
                            $"文字：{text.Text}，位置：{point.X}，宽度：{text.Bounds.Width}，卡片：{card.Bounds.Width}");
                    }
                    var map = texts.Single(text => text.Text!.StartsWith("战术强化训练", StringComparison.Ordinal));
                    Assert.True(map.Bounds.Height > 20);
                    Assert.Contains(texts, text => text.Text == "部队一");
                    Assert.Contains(texts, text => text.Text == "出阵中");
                }
                window.CaptureRenderedFrame()!.Save(Path.Combine(Path.GetTempPath(), "MATR-team-wrapping.png"));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
}
