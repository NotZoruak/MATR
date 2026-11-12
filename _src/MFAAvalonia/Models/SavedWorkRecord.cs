using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace MFAAvalonia.Models;

/// <summary>保存到本地的结构化工作记录，不依赖原始日志文件。</summary>
public sealed class SavedWorkRecord
{
    public string DisplayName { get; set; } = "";
    public string TaskName { get; set; } = "";
    /// <summary>任务 pipeline 入口，用于在用户修改备注后仍能识别同一任务。</summary>
    public string Entry { get; set; } = "";
    public string ConfigName { get; set; } = "";

    /// <summary>当前页签显示名称，仅用于界面显示，不写入保存文件。</summary>
    [JsonIgnore]
    public string DisplayConfigName { get; set; } = "";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
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
    public List<SavedWorkRecordSegment> Segments { get; set; } = [];

    [JsonIgnore]
    public string DateRangeText => StartDate == EndDate
        ? StartDate.ToString("yyyy-MM-dd")
        : $"{StartDate:yyyy-MM-dd}—{EndDate:yyyy-MM-dd}";

    [JsonIgnore]
    public string DurationText => Duration.TotalMinutes < 60
        ? $"{Math.Max(1, (int)Math.Ceiling(Duration.TotalMinutes))} 分钟"
        : $"{(int)Duration.TotalHours} 小时 {Duration.Minutes} 分";

    /// <summary>从运行记录创建本地保存记录。</summary>
    public static SavedWorkRecord FromWorkRecord(WorkRecord source, string displayName)
    {
        return new SavedWorkRecord
        {
            DisplayName = displayName,
            TaskName = source.TaskName,
            Entry = source.Entry,
            ConfigName = source.ConfigName,
            DisplayConfigName = source.DisplayConfigName,
            StartDate = source.StartTime.Date,
            EndDate = source.EndTime.Date,
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
            Segments = [SavedWorkRecordSegment.FromWorkRecord(source)],
        };
    }

    /// <summary>转换回工作记录，供右侧详情和继续合并使用。</summary>
    public WorkRecord ToWorkRecord()
    {
        var result = new WorkRecord
        {
            TaskName = TaskName,
            Entry = Entry,
            ConfigName = ConfigName,
            DisplayConfigName = DisplayConfigName,
            StartTime = Segments.Count > 0 ? Segments.Min(segment => segment.StartTime) : StartDate,
            EndTime = Segments.Count > 0 ? Segments.Max(segment => segment.EndTime) : EndDate,
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
}
