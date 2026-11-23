using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using MFAAvalonia.Services;
using MFAAvalonia.Views.Pages;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public sealed class TeamStatusCountdownTests
{
    [Fact]
    public async Task 远征倒计时位于结束时间下方且逐秒更新到完成()
    {
        await ExtensionUiTests.Session.Dispatch(() =>
        {
            var view = new TeamStatusView(Path.GetTempPath());
            var now = new DateTimeOffset(2026, 11, 23, 14, 0, 0, TimeSpan.FromHours(8));
            view.Tick(now);
            var snapshot = new GameOverviewLogParser.Snapshot();
            snapshot.Teams["2"] = new GameOverviewLogParser.TeamEntry("2", "2026-11-23 16:00:00", "2-1");
            view.SetSnapshot(snapshot);
            var cards = view.FindControl<Grid>("TeamCards")!;
            var button = Assert.IsType<Button>(cards.Children[1]);
            var details = Assert.IsType<StackPanel>(button.Content);
            var texts = details.Children.OfType<TextBlock>().ToArray();
            var endIndex = Array.FindIndex(texts, text => text.Text == "11-23 16:00:00");
            Assert.True(endIndex >= 0);
            Assert.Equal("剩余：02:00:00", texts[endIndex + 1].Text);
            Assert.Equal(texts[endIndex].FontSize, texts[endIndex + 1].FontSize);
            Assert.Equal(texts[endIndex].FontWeight, texts[endIndex + 1].FontWeight);
            Assert.Equal(texts[endIndex].Foreground, texts[endIndex + 1].Foreground);
            view.Tick(now.AddSeconds(1));
            Assert.Same(button, cards.Children[1]);
            Assert.Equal("剩余：01:59:59", texts[endIndex + 1].Text);
            view.Tick(now.AddHours(2));
            var completed = Assert.IsType<StackPanel>(Assert.IsType<Button>(cards.Children[1]).Content);
            Assert.DoesNotContain(completed.Children.OfType<TextBlock>(), text => text.Text?.StartsWith("剩余：", StringComparison.Ordinal) == true || text.Text == "11-23 16:00:00");
            Assert.Equal("2", snapshot.Teams["2"].Status);
        }, CancellationToken.None);
    }
}
