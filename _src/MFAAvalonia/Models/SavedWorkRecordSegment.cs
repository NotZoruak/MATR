using System;
using System.Collections.Generic;
using System.Linq;

namespace MFAAvalonia.Models;

/// <summary>保存记录中的一段原始工作记录及其精确时间范围。</summary>
public sealed class SavedWorkRecordSegment
{
    public string TaskName { get; set; } = "";
    /// <summary>任务 pipeline 入口，用于兼容不同任务备注。</summary>
    public string Entry { get; set; } = "";
    public string ConfigName { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public string Status { get; set; } = "成功";
    public int SortieCount { get; set; }
    public int MarchCount { get; set; }
    public int RoundCount { get; set; }
    public int FlowerBrushCount { get; set; }
    public int ReturnHomeCount { get; set; }
    public bool HasInterrupt { get; set; }
    public Dictionary<string, int> ResourceGains { get; set; } = [];
    public List<SwordDrop> SwordDrops { get; set; } = [];
    public List<string> SkippedSilhouettes { get; set; } = [];
    public Dictionary<string, int> LogisticsCounts { get; set; } = [];
    public List<LogisticsDispatch> LogisticsDispatches { get; set; } = [];
    public List<LogisticsRepair> LogisticsRepairs { get; set; } = [];
    public List<LogisticsNaibanOutfit> LogisticsNaibanOutfits { get; set; } = [];
    public List<SpecialEvent> SpecialEvents { get; set; } = [];

    /// <summary>从运行记录创建保存段。</summary>
    public static SavedWorkRecordSegment FromWorkRecord(WorkRecord source)
    {
        return new SavedWorkRecordSegment
        {
            TaskName = source.TaskName,
            Entry = source.Entry,
            ConfigName = source.ConfigName,
            StartTime = source.StartTime,
            EndTime = source.EndTime,
            Duration = source.Duration,
            Status = source.Status,
            SortieCount = source.SortieCount,
            MarchCount = source.MarchCount,
            RoundCount = source.RoundCount,
            FlowerBrushCount = source.FlowerBrushCount,
            ReturnHomeCount = source.ReturnHomeCount,
            HasInterrupt = source.HasInterrupt,
            ResourceGains = new Dictionary<string, int>(source.ResourceGains),
            SwordDrops = source.SwordDrops.ToList(),
            SkippedSilhouettes = source.SkippedSilhouettes.ToList(),
            LogisticsCounts = new Dictionary<string, int>(source.LogisticsCounts),
            LogisticsDispatches = source.LogisticsDispatches.ToList(),
            LogisticsRepairs = source.LogisticsRepairs.ToList(),
            LogisticsNaibanOutfits = source.LogisticsNaibanOutfits.ToList(),
            SpecialEvents = source.SpecialEvents.ToList(),
        };
    }

    /// <summary>转换回运行记录，供合并统计使用。</summary>
    public WorkRecord ToWorkRecord()
    {
        var result = new WorkRecord
        {
            TaskName = TaskName,
            Entry = Entry,
            ConfigName = ConfigName,
            StartTime = StartTime,
            EndTime = EndTime,
            DurationOverride = Duration,
            Status = Status,
            HasInterrupt = HasInterrupt,
            SortieCount = SortieCount,
            MarchCount = MarchCount,
            RoundCount = RoundCount,
            FlowerBrushCount = FlowerBrushCount,
            ReturnHomeCount = ReturnHomeCount,
        };
        foreach (var item in ResourceGains)
            result.ResourceGains[item.Key] = item.Value;
        result.SwordDrops.AddRange(SwordDrops);
        result.SkippedSilhouettes.AddRange(SkippedSilhouettes);
        foreach (var item in LogisticsCounts)
            result.LogisticsCounts[item.Key] = item.Value;
        result.LogisticsDispatches.AddRange(LogisticsDispatches);
        result.LogisticsRepairs.AddRange(LogisticsRepairs);
        result.LogisticsNaibanOutfits.AddRange(LogisticsNaibanOutfits);
        result.SpecialEvents.AddRange(SpecialEvents);
        return result;
    }

    /// <summary>从旧版保存记录生成没有精确时间的兼容保存段。</summary>
    public static SavedWorkRecordSegment FromLegacyRecord(SavedWorkRecord source)
    {
        var record = source.ToWorkRecord();
        return FromWorkRecord(record);
    }
}
