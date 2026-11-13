using MFAAvalonia.Models;
using MFAAvalonia.Services;
using MFAAvalonia.ViewModels.Pages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class WorkRecordSkippedSilhouetteTests
{
    private static List<string> Skipped(WorkRecord record) => record.SkippedSilhouettes;

    [Fact]
    public void 仅采集当前非目标刷新日志并过滤短时重复()
    {
        var start = new DateTime(2026, 11, 12, 12, 0, 0);
        var record = Assert.Single(WorkRecordBuilder.Build([
            new LogEntry(start, "INF", "开始任务：战术强化"),
            new LogEntry(start.AddSeconds(1), "INF", "[战术强化] 当前剪影：厚藤四郎，得分 99.00%，非目标刀剑，刷新剪影"),
            new LogEntry(start.AddSeconds(2), "INF", "[战术强化] 当前剪影：厚藤四郎，得分 99.10%，非目标刀剑，刷新剪影"),
            new LogEntry(start.AddSeconds(5), "INF", "[战术强化] 当前剪影：小狐丸，得分 99.00%，目标刀剑，继续出阵"),
            new LogEntry(start.AddSeconds(10), "INF", "[战术强化] 当前剪影：平野藤四郎，得分 98.00%，非目标刀剑，刷新剪影"),
            new LogEntry(start.AddSeconds(12), "INF", "[战术强化] 当前剪影：旧记录刀剑，得分 98.00%，非目标刀剑，准备刷新"),
            new LogEntry(start.AddSeconds(15), "INF", "停止前状态：SUCCEEDED")
        ]));
        Assert.Equal(new[] { "厚藤四郎", "平野藤四郎" }, Skipped(record));
        Assert.True(record.HasRun);
        Assert.Empty(record.SwordDrops);
    }

    [Fact]
    public void 保存重载与合并应保留跳过剪影且不影响刀剑掉落()
    {
        var start = new DateTime(2026, 11, 12, 12, 0, 0);
        var first = new WorkRecord { TaskName = "战术强化", Entry = "TacticalTraining", StartTime = start, EndTime = start.AddMinutes(1) };
        Skipped(first).Add("厚藤四郎");
        first.SwordDrops.Add(new SwordDrop("太刀", "小狐丸"));
        var saved = SavedWorkRecord.FromWorkRecord(first, "记录一");
        var loaded = JsonSerializer.Deserialize<SavedWorkRecord>(JsonSerializer.Serialize(saved))!;
        Assert.Equal(new[] { "厚藤四郎" }, Skipped(loaded.ToWorkRecord()));
        var second = new WorkRecord { TaskName = "战术强化", Entry = "TacticalTraining", StartTime = start.AddMinutes(2), EndTime = start.AddMinutes(3) };
        Skipped(second).Add("平野藤四郎");
        var merged = SavedWorkRecordService.Merge(new[] { loaded, SavedWorkRecord.FromWorkRecord(second, "记录二") }, "合并");
        Assert.Equal(new[] { "厚藤四郎", "平野藤四郎" }, Skipped(merged.ToWorkRecord()));
        Assert.Single(merged.ToWorkRecord().SwordDrops);
        Assert.Empty(Skipped(JsonSerializer.Deserialize<SavedWorkRecord>("{}")!.ToWorkRecord()));
    }

    [Fact]
    public void 仅有跳过剪影时也应显示任务收获并按刀种和刀名汇总()
    {
        var record = new WorkRecord();
        Skipped(record).AddRange(["厚藤四郎", "厚藤四郎", "平野藤四郎"]);
        var model = new WorkRecordsViewModel { SelectedRecord = record };
        Assert.True(model.SelectedHasOutcomes);
        Assert.Equal("跳过剪影（×3）", model.SelectedSkippedSilhouetteTotalText);
        var group = Assert.Single(model.SelectedSkippedSilhouetteGroups);
        Assert.Equal("厚藤四郎（x2），平野藤四郎（x1）", group.SilhouettesText);
    }
}
