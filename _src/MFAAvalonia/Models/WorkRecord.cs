using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MFAAvalonia.Models;

/// <summary>一条运行记录：队列中一个任务从开始到结束</summary>
public sealed class WorkRecord
{
    /// <summary>任务显示名称（可能来自任务备注或资源 label）。</summary>
    public string TaskName { get; set; } = "";

    /// <summary>配置来源（来自日志 cfg= 块）</summary>
    public string ConfigName { get; set; } = "";

    /// <summary>当前页签显示名称，仅用于界面显示，不参与保存和合并。</summary>
    [JsonIgnore]
    public string DisplayConfigName { get; set; } = "";

    /// <summary>入口 pipeline（如 Underground）</summary>
    public string Entry { get; set; } = "";

    /// <summary>开始时间（任务定义行时间）</summary>
    public DateTime StartTime { get; set; }

    /// <summary>结束时间（下一任务定义行或队列停止行时间）</summary>
    public DateTime EndTime { get; set; }

    /// <summary>总耗时</summary>
    public TimeSpan Duration => DurationOverride ?? (EndTime - StartTime);

    /// <summary>保存记录或合并记录的累计时长覆盖值，普通日志记录为空。</summary>
    public TimeSpan? DurationOverride { get; set; }

    /// <summary>运行结果：进行中/成功/中断/手动停止/失败/未开始；成功和手动停止在界面统一显示为结束。</summary>
    public string Status { get; set; } = "进行中";

    /// <summary>界面显示的状态名称。</summary>
    [JsonIgnore]
    public string DisplayStatus => Status is "成功" or "手动停止" ? "结束" : Status;

    /// <summary>界面显示的状态文字颜色。</summary>
    [JsonIgnore]
    public string StatusForeground => DisplayStatus switch
    {
        "结束" => "#15803D",
        "进行中" => "#2563EB",
        "失败" => "#B42318",
        "中断" => "#9F1239",
        _ => "#667085",
    };

    /// <summary>界面显示的状态标签背景颜色。</summary>
    [JsonIgnore]
    public string StatusBackground => DisplayStatus switch
    {
        "结束" => "#F0FDF4",
        "进行中" => "#EFF6FF",
        "失败" => "#FEF3F2",
        "中断" => "#FFF1F2",
        _ => "#F2F4F7",
    };

    /// <summary>记录期间是否出现中断事件（覆盖最终状态）</summary>
    public bool HasInterrupt { get; set; }

    /// <summary>任务是否实际开始执行过（存在「开始任务」行）</summary>
    public bool HasStarted { get; set; }

    /// <summary>是否已由队列停止状态明确结束，不再接收后续关联词条。</summary>
    [JsonIgnore]
    public bool IsClosedByStopStatus { get; set; }

    /// <summary>任务是否产生过任何词条（有实际运行痕迹）</summary>
    public bool HasRun =>
        SortieCount > 0
        || MarchCount > 0
        || RoundCount > 0
        || FlowerBrushCount > 0
        || ReturnHomeCount > 0
        || ResourceGains.Count > 0
        || SwordDrops.Count > 0
        || LogisticsCounts.Count > 0
        || SpecialEvents.Count > 0;

    /// <summary>列表行文本：08-18 12:00—13:27 地下城 (中断)</summary>
    public string ListLine =>
        $"{StartTime:MM-dd HH:mm}—{EndTimeText}  {TaskName}" +
        (DisplayStatus == "结束" ? "" : $" ({DisplayStatus})");

    /// <summary>结束时间文本：与开始时间同日时只显示时分，跨天时补上月日。</summary>
    public string EndTimeText =>
        EndTime != default && EndTime.Date != StartTime.Date
            ? EndTime.ToString("MM-dd HH:mm")
            : EndTime.ToString("HH:mm");

    /// <summary>记录列表中的时间文本</summary>
    public string ListTimeText => $"{StartTime:MM-dd HH:mm}–{EndTimeText} · {DurationText}";

    /// <summary>耗时文本：1 小时 27 分 / 35 分钟</summary>
    public string DurationText =>
        Duration.TotalMinutes < 60
            ? $"{Math.Max(1, (int)Math.Ceiling(Duration.TotalMinutes))} 分钟"
            : $"{(int)Duration.TotalHours} 小时 {Duration.Minutes} 分";

    /// <summary>出阵次数（「出阵」词条计数）</summary>
    public int SortieCount { get; set; }

    /// <summary>行军次数（「点击行军」词条计数）</summary>
    public int MarchCount { get; set; }

    /// <summary>完成圈数（仅统计「完成一圈」词条）</summary>
    public int RoundCount { get; set; }

    /// <summary>出阵刷花次数（非「后勤」前缀的「刷花」词条计数）</summary>
    public int FlowerBrushCount { get; set; }

    /// <summary>返回本丸次数（由确认返回本丸的日志统计）</summary>
    public int ReturnHomeCount { get; set; }

    /// <summary>资源收获：资源名 → 累计数量（小判箱掉落按次数计入）</summary>
    public Dictionary<string, int> ResourceGains { get; } = [];

    /// <summary>刀剑掉落明细</summary>
    public List<SwordDrop> SwordDrops { get; } = [];

    /// <summary>后勤记录：行为词 → 次数（[后勤] 前缀词条）</summary>
    public Dictionary<string, int> LogisticsCounts { get; } = [];

    /// <summary>派遣远征明细（时间/部队/地图）</summary>
    public List<LogisticsDispatch> LogisticsDispatches { get; } = [];

    /// <summary>开始修复明细（时间/刀剑名/资源消耗）</summary>
    public List<LogisticsRepair> LogisticsRepairs { get; } = [];

    /// <summary>内番服识别明细（时间/刀剑名）</summary>
    public List<LogisticsNaibanOutfit> LogisticsNaibanOutfits { get; } = [];

    /// <summary>特殊情况：Warning 词条与中断事件（时间/描述）</summary>
    public List<SpecialEvent> SpecialEvents { get; } = [];
}

/// <summary>刀剑掉落明细条目</summary>
public sealed record SwordDrop(string SwordType, string SwordName);

/// <summary>派遣远征明细：部队 → 地图</summary>
public sealed record LogisticsDispatch(DateTime Time, string Unit, string Map);

/// <summary>开始修复明细</summary>
public sealed record LogisticsRepair(DateTime Time, string SwordName, int Wood, int Steel, int Coolant, int Whetstone);

/// <summary>内番服识别明细：SwordName 为空表示本次安排内番时没有识别到内番服立绘</summary>
public sealed record LogisticsNaibanOutfit(DateTime Time, string SwordName);

/// <summary>特殊事件（Warning 词条或中断事件）</summary>
public sealed record SpecialEvent(DateTime Time, string Description);
