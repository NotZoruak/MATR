using MFAAvalonia.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MFAAvalonia.Services;

/// <summary>保存记录的命名和合并业务规则。</summary>
public static class SavedWorkRecordService
{
    /// <summary>合并同一任务的运行记录；去重以配置与时间为维度。</summary>
    public static SavedWorkRecord Merge(IEnumerable<WorkRecord> sources, string displayName)
    {
        var records = sources
            .GroupBy(record => (record.ConfigName, record.StartTime, record.EndTime))
            .Select(group => group.First())
            .ToList();
        if (records.Count == 0)
            throw new ArgumentException("至少需要一条记录才能合并。", nameof(sources));

        if (records.Any(record => !IsSameTask(record, records[0])))
            throw new InvalidOperationException("只能合并同名任务。");

        var result = MergeRecords(records, displayName);
        result.Segments = records
            .Select(SavedWorkRecordSegment.FromWorkRecord)
            .ToList();
        return result;
    }

    /// <summary>合并已有保存记录，并按配置与精确时间去重。</summary>
    public static SavedWorkRecord Merge(IEnumerable<SavedWorkRecord> sources, string displayName)
    {
        var savedRecords = sources.ToList();
        if (savedRecords.Count == 0)
            throw new ArgumentException("至少需要一条记录才能合并。", nameof(sources));

        if (savedRecords.Any(record => !IsSameTask(record, savedRecords[0])))
            throw new InvalidOperationException("只能合并同名任务。");

        var segments = savedRecords
            .SelectMany(record => record.Segments.Count > 0
                ? record.Segments
                : [SavedWorkRecordSegment.FromLegacyRecord(record)])
            .GroupBy(segment => (segment.ConfigName, segment.StartTime, segment.EndTime))
            .Select(group => group.First())
            .ToList();
        var result = MergeRecords(segments.Select(segment => segment.ToWorkRecord()).ToList(), displayName);
        result.Segments = segments;
        return result;
    }

    private static SavedWorkRecord MergeRecords(IReadOnlyCollection<WorkRecord> records, string displayName)
    {
        var taskName = records.First().TaskName;
        if (records.Any(record => !IsSameTask(record, records.First())))
            throw new InvalidOperationException("只能合并同名任务。");

        var configNames = records
            .Select(record => record.ConfigName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var result = new SavedWorkRecord
        {
            DisplayName = displayName,
            TaskName = taskName,
            Entry = records.First().Entry,
            ConfigName = configNames.Count == 1 ? configNames[0] : "",
            StartDate = records.Min(record => record.StartTime.Date),
            EndDate = records.Max(record => record.EndTime.Date),
            Duration = TimeSpan.FromTicks(records.Sum(record => record.Duration.Ticks)),
            Status = MergeStatus(records),
            SortieCount = records.Sum(record => record.SortieCount),
            MarchCount = records.Sum(record => record.MarchCount),
            RoundCount = records.Sum(record => record.RoundCount),
            FlowerBrushCount = records.Sum(record => record.FlowerBrushCount),
            ReturnHomeCount = records.Sum(record => record.ReturnHomeCount),
            HasInterrupt = records.Any(record => record.HasInterrupt),
        };

        foreach (var record in records)
        {
            foreach (var item in record.ResourceGains)
                result.ResourceGains[item.Key] = result.ResourceGains.GetValueOrDefault(item.Key) + item.Value;
            result.SwordDrops.AddRange(record.SwordDrops);
            result.SkippedSilhouettes.AddRange(record.SkippedSilhouettes);
            foreach (var item in record.LogisticsCounts)
                result.LogisticsCounts[item.Key] = result.LogisticsCounts.GetValueOrDefault(item.Key) + item.Value;
            result.LogisticsDispatches.AddRange(record.LogisticsDispatches);
            result.LogisticsRepairs.AddRange(record.LogisticsRepairs);
            result.LogisticsNaibanOutfits.AddRange(record.LogisticsNaibanOutfits);
            result.SpecialEvents.AddRange(record.SpecialEvents);
        }

        result.LogisticsDispatches = result.LogisticsDispatches.OrderBy(item => item.Time).ToList();
        result.LogisticsRepairs = result.LogisticsRepairs.OrderBy(item => item.Time).ToList();
        result.LogisticsNaibanOutfits = result.LogisticsNaibanOutfits.OrderBy(item => item.Time).ToList();
        result.SpecialEvents = result.SpecialEvents.OrderBy(item => item.Time).ToList();
        return result;
    }

    /// <summary>判断任务名称是否属于同一个任务，兼容显示名称调整前后的历史记录。</summary>
    private static bool IsSameTask(WorkRecord left, WorkRecord right)
    {
        if (!string.IsNullOrWhiteSpace(left.Entry) && !string.IsNullOrWhiteSpace(right.Entry))
            return string.Equals(left.Entry, right.Entry, StringComparison.Ordinal);

        return string.Equals(left.TaskName, right.TaskName, StringComparison.Ordinal);
    }

    private static bool IsSameTask(SavedWorkRecord left, SavedWorkRecord right)
    {
        if (!string.IsNullOrWhiteSpace(left.Entry) && !string.IsNullOrWhiteSpace(right.Entry))
            return string.Equals(left.Entry, right.Entry, StringComparison.Ordinal);

        return string.Equals(left.TaskName, right.TaskName, StringComparison.Ordinal);
    }

    /// <summary>保存一条运行记录。</summary>
    public static SavedWorkRecord Save(WorkRecord source, string displayName) =>
        SavedWorkRecord.FromWorkRecord(source, displayName);

    /// <summary>从已有名称中生成不重复的显示名称。</summary>
    public static string CreateUniqueName(string requestedName, IEnumerable<string> existingNames)
    {
        var baseName = requestedName.Trim();
        var names = existingNames.ToHashSet(StringComparer.Ordinal);
        if (!names.Contains(baseName))
            return baseName;

        var number = 1;
        while (names.Contains($"{baseName}（{number}）"))
            number++;
        return $"{baseName}（{number}）";
    }

    private static string MergeStatus(IEnumerable<WorkRecord> records)
    {
        var statuses = records.Select(record => record.Status).ToHashSet(StringComparer.Ordinal);
        if (statuses.Contains("中断")) return "中断";
        if (statuses.Contains("失败")) return "失败";
        if (statuses.Contains("进行中")) return "进行中";
        if (statuses.Contains("手动停止")) return "手动停止";
        return "成功";
    }
}
