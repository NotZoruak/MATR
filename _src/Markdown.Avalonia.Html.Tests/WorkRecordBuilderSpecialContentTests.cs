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

    [Fact]
    public void Build_常驻作战前缀仍归入合战场任务记录()
    {
        var start = new DateTime(2026, 9, 20, 10, 0, 0);
        var records = WorkRecordBuilder.Build(
        [
            new LogEntry(start, "INF", "名称=[合战场] 入口=[Sortie]"),
            new LogEntry(start.AddSeconds(1), "INF", "开始任务：合战场"),
            new LogEntry(start.AddSeconds(2), "INF", "[Record] [常驻作战] 出阵"),
            new LogEntry(start.AddSeconds(3), "INF", "[Record] [常驻作战] 完成一圈"),
            new LogEntry(start.AddSeconds(4), "INF", "停止前状态：SUCCEEDED"),
        ]);

        var record = Assert.Single(records);

        Assert.Equal("Sortie", record.Entry);
        Assert.Equal(1, record.SortieCount);
        Assert.Equal(1, record.RoundCount);
    }

    [Fact]
    public void Build_大阪挖地前缀归入地下城任务记录()
    {
        var start = new DateTime(2026, 9, 20, 10, 0, 0);
        var records = WorkRecordBuilder.Build(
        [
            new LogEntry(start, "INF", "名称=[地下城] 入口=[Underground]"),
            new LogEntry(start.AddSeconds(1), "INF", "开始任务：地下城"),
            new LogEntry(start.AddSeconds(2), "INF", "[Record] [大阪挖地] 出阵"),
            new LogEntry(start.AddSeconds(3), "INF", "[Record] [大阪挖地] 点击行军"),
            new LogEntry(start.AddSeconds(4), "INF", "[Record] [大阪挖地] 完成一圈"),
            new LogEntry(start.AddSeconds(5), "INF", "停止前状态：SUCCEEDED"),
        ]);

        var record = Assert.Single(records);

        Assert.Equal("Underground", record.Entry);
        Assert.Equal(1, record.SortieCount);
        Assert.Equal(1, record.MarchCount);
        Assert.Equal(1, record.RoundCount);
    }
}
