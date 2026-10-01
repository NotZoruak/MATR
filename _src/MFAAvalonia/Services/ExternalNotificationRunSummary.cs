using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MFAAvalonia.Services;

/// <summary>保存一个实例在一次任务运行中的外部通知汇总数据。</summary>
public sealed class ExternalNotificationRunSummary
{
    private readonly Dictionary<string, ExternalNotificationTaskSummary> _tasks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _resources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, int>> _swordDrops = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _logisticsCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _expeditionMaps = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _specialCases = new(StringComparer.Ordinal);
    private readonly List<string> _naibanDetails = [];
    private readonly List<string> _logisticsRepairDetails = [];

    public ExternalNotificationRunSummary(string instanceName, DateTime startedAt)
    {
        InstanceName = string.IsNullOrWhiteSpace(instanceName) ? "当前实例" : instanceName;
        StartedAt = startedAt;
    }

    public string InstanceName { get; }
    public DateTime StartedAt { get; }
    public IReadOnlyDictionary<string, ExternalNotificationTaskSummary> Tasks => new ReadOnlyDictionary<string, ExternalNotificationTaskSummary>(_tasks);
    public IReadOnlyDictionary<string, int> Resources => new ReadOnlyDictionary<string, int>(_resources);
    public IReadOnlyDictionary<string, Dictionary<string, int>> SwordDrops => new ReadOnlyDictionary<string, Dictionary<string, int>>(_swordDrops);
    public IReadOnlyDictionary<string, int> LogisticsCounts => new ReadOnlyDictionary<string, int>(_logisticsCounts);
    public IReadOnlyDictionary<string, int> ExpeditionMaps => new ReadOnlyDictionary<string, int>(_expeditionMaps);
    public IReadOnlyDictionary<string, int> SpecialCases => new ReadOnlyDictionary<string, int>(_specialCases);
    public IReadOnlyList<string> NaibanDetails => _naibanDetails;
    public IReadOnlyList<string> LogisticsRepairDetails => _logisticsRepairDetails;

    public void RecordTask(string taskName, int repeatCount, TimeSpan elapsed)
    {
        if (string.IsNullOrWhiteSpace(taskName))
            return;

        if (!_tasks.TryGetValue(taskName, out var task))
        {
            task = new ExternalNotificationTaskSummary(taskName);
            _tasks.Add(taskName, task);
        }

        task.Add(Math.Max(1, repeatCount), elapsed);
    }

    public void RecordResource(string resourceName, int count)
    {
        if (!string.IsNullOrWhiteSpace(resourceName) && count > 0)
            AddCount(_resources, resourceName, count);
    }

    public void RecordSwordDrop(string swordType, string swordName, int count = 1)
    {
        if (string.IsNullOrWhiteSpace(swordType) || string.IsNullOrWhiteSpace(swordName) || count <= 0)
            return;

        if (!_swordDrops.TryGetValue(swordType, out var swords))
        {
            swords = new Dictionary<string, int>(StringComparer.Ordinal);
            _swordDrops.Add(swordType, swords);
        }

        AddCount(swords, swordName, count);
    }

    public void RecordLogisticsCount(string name, int count = 1) => AddNamedCount(_logisticsCounts, name, count);
    public void RecordExpedition(string mapName, int count = 1) => AddNamedCount(_expeditionMaps, mapName, count);
    public void RecordSpecialCase(string name, int count = 1) => AddNamedCount(_specialCases, name, count);

    public void RecordNaibanDetail(string detail) => AddDetail(_naibanDetails, detail);
    public void RecordLogisticsRepairDetail(string detail) => AddDetail(_logisticsRepairDetails, detail);

    private static void AddNamedCount(Dictionary<string, int> target, string name, int count)
    {
        if (!string.IsNullOrWhiteSpace(name) && count > 0)
            AddCount(target, name, count);
    }

    private static void AddCount(Dictionary<string, int> target, string name, int count) =>
        target[name] = target.GetValueOrDefault(name) + count;

    private static void AddDetail(List<string> target, string detail)
    {
        if (!string.IsNullOrWhiteSpace(detail))
            target.Add(detail);
    }
}

/// <summary>保存同名任务聚合后的配置重复次数和耗时。</summary>
public sealed class ExternalNotificationTaskSummary(string taskName)
{
    public string TaskName { get; } = taskName;
    public int RepeatCount { get; private set; }
    public TimeSpan Elapsed { get; private set; }

    public void Add(int repeatCount, TimeSpan elapsed)
    {
        RepeatCount += repeatCount;
        Elapsed += elapsed;
    }
}

/// <summary>保存切换实例后连续运行的全部实例分段。</summary>
public sealed class ExternalNotificationRunSession(DateTime startedAt)
{
    private readonly List<ExternalNotificationCompletedSegment> _segments = [];

    public DateTime StartedAt { get; } = startedAt;
    public IReadOnlyList<ExternalNotificationCompletedSegment> Segments => _segments;

    public static ExternalNotificationRunSession FromCompleted(ExternalNotificationRunSummary summary, DateTime completedAt)
    {
        var session = new ExternalNotificationRunSession(summary.StartedAt);
        session.AddCompletedSegment(summary, completedAt);
        return session;
    }

    public void AddCompletedSegment(ExternalNotificationRunSummary summary, DateTime completedAt) =>
        _segments.Add(new ExternalNotificationCompletedSegment(summary, completedAt));

    public DateTime CompletedAt => _segments.Count == 0 ? StartedAt : _segments[^1].CompletedAt;
}

/// <summary>表示一个实例分段及其完成时间。</summary>
public sealed record ExternalNotificationCompletedSegment(ExternalNotificationRunSummary Summary, DateTime CompletedAt);

/// <summary>表示结束推送中由用户选择的可选内容。</summary>
public sealed class ExternalNotificationReportOptions
{
    public bool IncludeTaskHarvest { get; init; }
    public bool IncludeLogistics { get; init; }
    public bool IncludeSpecialCases { get; init; }
}
