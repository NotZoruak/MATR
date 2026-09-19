using MFAAvalonia.Services;
using System;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class WorkRecordBuilderSpecialContentTests
{
    [Fact]
    public void Build_Info级特殊记录归入特殊情况()
    {
        var start = new DateTime(2026, 9, 19, 10, 0, 0);
        var records = WorkRecordBuilder.Build(
        [
            new LogEntry(start, "INF", "[cfg=Default][inst=一号机/default] 开始任务：联队战", "Default", "default"),
            new LogEntry(start.AddSeconds(1), "INF", "[cfg=Default][inst=一号机/default] [Record][Special] [联队战] 无票终止", "Default", "default"),
            new LogEntry(start.AddSeconds(2), "INF", "[cfg=Default][inst=一号机/default] 停止前状态：SUCCEEDED", "Default", "default"),
        ]);

        var record = Assert.Single(records);
        var specialEvent = Assert.Single(record.SpecialEvents);

        Assert.Equal("无票终止", specialEvent.Description);
        Assert.Equal(start.AddSeconds(1), specialEvent.Time);
        Assert.Equal("成功", record.Status);
    }

    [Fact]
    public void Build_普通Info记录不归入特殊情况()
    {
        var start = new DateTime(2026, 9, 19, 10, 0, 0);
        var records = WorkRecordBuilder.Build(
        [
            new LogEntry(start, "INF", "开始任务：联队战"),
            new LogEntry(start.AddSeconds(1), "INF", "[Record] [联队战] 完成一圈"),
            new LogEntry(start.AddSeconds(2), "INF", "停止前状态：SUCCEEDED"),
        ]);

        var record = Assert.Single(records);

        Assert.Equal(1, record.RoundCount);
        Assert.Empty(record.SpecialEvents);
    }

    [Fact]
    public void Build_Warning级记录仍归入特殊情况()
    {
        var start = new DateTime(2026, 9, 19, 10, 0, 0);
        var records = WorkRecordBuilder.Build(
        [
            new LogEntry(start, "INF", "开始任务：联队战"),
            new LogEntry(start.AddSeconds(1), "WRN", "[Record] [联队战] 队长重伤撤退"),
            new LogEntry(start.AddSeconds(2), "INF", "停止前状态：SUCCEEDED"),
        ]);

        var record = Assert.Single(records);
        var specialEvent = Assert.Single(record.SpecialEvents);

        Assert.Equal("队长重伤撤退", specialEvent.Description);
    }
}
