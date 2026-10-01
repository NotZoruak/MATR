using System;
using MFAAvalonia.Services;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class ExternalNotificationReportFormatterTests
{
    [Fact]
    public void 应汇总任务耗时并按约定顺序输出收获()
    {
        var summary = new ExternalNotificationRunSummary("实例 A", new DateTime(2026, 10, 1, 8, 0, 0));
        summary.RecordTask("海陆联队", 99, TimeSpan.FromHours(2));
        summary.RecordTask("海陆联队", 99, TimeSpan.FromHours(1));
        summary.RecordResource("木炭", 200);
        summary.RecordResource("小判箱", 3);
        summary.RecordSwordDrop("太刀", "三日月宗近");
        summary.RecordSwordDrop("短刀", "厚藤四郎", 2);

        var report = ExternalNotificationReportFormatter.Format(
            ExternalNotificationRunSession.FromCompleted(summary, summary.StartedAt.AddHours(3)),
            new ExternalNotificationReportOptions { IncludeTaskHarvest = true });

        Assert.Contains("任务已全部完成", report);
        Assert.Contains("总用时：3小时", report);
        Assert.Contains("海陆联队 ×198：3小时", report);
        Assert.Contains("资源：木炭×200，小判箱×3", report);
        Assert.Contains("短刀：厚藤四郎×2", report);
        Assert.Contains("太刀：三日月宗近×1", report);
        Assert.True(report.IndexOf("短刀：", StringComparison.Ordinal) < report.IndexOf("太刀：", StringComparison.Ordinal));
    }

    [Fact]
    public void 多实例会话应显示分段并使用首尾时间计算总用时()
    {
        var first = new ExternalNotificationRunSummary("实例 A", new DateTime(2026, 10, 1, 8, 0, 0));
        first.RecordTask("海陆联队", 99, TimeSpan.FromHours(2));
        var second = new ExternalNotificationRunSummary("实例 B", new DateTime(2026, 10, 1, 10, 5, 0));
        second.RecordTask("海陆联队", 99, TimeSpan.FromHours(2));

        var session = new ExternalNotificationRunSession(first.StartedAt);
        session.AddCompletedSegment(first, new DateTime(2026, 10, 1, 10, 0, 0));
        session.AddCompletedSegment(second, new DateTime(2026, 10, 1, 12, 10, 0));

        var report = ExternalNotificationReportFormatter.Format(session, new ExternalNotificationReportOptions());

        Assert.Contains("总用时：4小时10分", report);
        Assert.Contains("实例 A", report);
        Assert.Contains("实例 B", report);
    }
}
