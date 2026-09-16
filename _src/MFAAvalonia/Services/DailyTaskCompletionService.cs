using MFAAvalonia.Helper;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace MFAAvalonia.Services;

/// <summary>
/// 管理日课项目按游戏日完成一次的持久化台账，以及本次运行期间的临时跳过状态。
/// 台账与运行期状态都按实例分别保存，多实例并行时互不影响。
/// </summary>
public static class DailyTaskCompletionService
{
    private static readonly TimeOnly ResetTime = new(5, 0);
    private static readonly object SyncRoot = new();
    private const string CompletionLogFileName = "daily-task-completion.log";

    /// <summary>
    /// 实例标识缺失时的兜底键。定位不到执行实例时读取与写入都落在该键上，
    /// 保证同一次运行内的判断与记录仍然一致。
    /// </summary>
    private const string UnknownInstanceKey = "unknown";

    /// <summary>
    /// 旧版台账归属的实例键。旧台账只有「游戏日 + 项目」两列，没有实例信息；
    /// 多实例支持之前只有首个实例（default）在使用，因此统一归入该实例，
    /// 避免升级当天把已完成的项目重复执行一遍。
    /// </summary>
    private const string LegacyInstanceKey = "default";

    private static readonly IReadOnlyDictionary<string, string> LegacyItemAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["login_reward"] = "LoginReward",
            ["warm_gift"] = "WarmGift",
            ["mix"] = "Mix",
            ["forge"] = "Forge",
            ["disassemble"] = "Disassemble",
            ["drill"] = "Drill",
            ["reward"] = "Reward",
            ["mail"] = "Mail",
            ["sync_logistics"] = "SyncLogistics",
        };

    private static readonly object RunStateLock = new();
    private static readonly Dictionary<string, HashSet<string>> RunSkippedItems = new(StringComparer.Ordinal);

    /// <summary>当前游戏日完成记录发生变化时通知界面刷新；事件参数为发生变化的实例键。</summary>
    public static event EventHandler<string>? CompletionChanged;

    /// <summary>把实例标识规范化为台账与运行期状态共用的实例键。</summary>
    public static string NormalizeInstanceKey(string? instanceId)
    {
        return string.IsNullOrWhiteSpace(instanceId)
            ? UnknownInstanceKey
            : instanceId.Trim();
    }

    /// <summary>判断指定实例的日课项目在当前游戏日是否仍应执行。</summary>
    public static bool ShouldRun(string? instanceId, string item, DateTime now)
    {
        return ShouldRun(instanceId, item, 1, now);
    }

    /// <summary>判断指定实例的日课项目在当前游戏日的完成次数是否尚未达到要求。</summary>
    public static bool ShouldRun(string? instanceId, string item, int requiredCount, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(item))
            throw new ArgumentException("日课项目标识不能为空。", nameof(item));
        if (requiredCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(requiredCount), "完成次数必须大于零。");

        return GetCompletedCount(instanceId, item, now) < requiredCount;
    }

    /// <summary>获取指定实例的日课项目在当前游戏日的进度记录数。</summary>
    public static int GetCompletedCount(string? instanceId, string item, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(item))
            throw new ArgumentException("日课项目标识不能为空。", nameof(item));

        var instanceKey = NormalizeInstanceKey(instanceId);
        var canonicalItem = NormalizeItem(item);
        var gameDay = GetGameDay(now);
        lock (SyncRoot)
        {
            return CountRecords(ReadRecords(), instanceKey, canonicalItem, gameDay);
        }
    }

    /// <summary>
    /// 为指定实例追加一次日课项目的进度记录，返回写入后当前游戏日的记录数。
    /// 供锻刀、演练等按游戏日累计多次的项目使用，达到 requiredCount 后不再写入。
    /// </summary>
    public static int RecordProgress(string? instanceId, string item, int requiredCount, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(item))
            throw new ArgumentException("日课项目标识不能为空。", nameof(item));
        if (requiredCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(requiredCount), "完成次数必须大于零。");

        var instanceKey = NormalizeInstanceKey(instanceId);
        var canonicalItem = NormalizeItem(item);
        var gameDay = GetGameDay(now);
        int completedCount;
        lock (SyncRoot)
        {
            var records = ReadRecords();
            completedCount = CountRecords(records, instanceKey, canonicalItem, gameDay);
            if (completedCount >= requiredCount)
                return completedCount;

            AppendRecord(gameDay, instanceKey, canonicalItem);
            completedCount++;
        }

        CompletionChanged?.Invoke(null, instanceKey);
        return completedCount;
    }

    /// <summary>获取指定实例在当前游戏日已有记录的项目标识；未达到上限的进度项目同样会被返回。</summary>
    public static IReadOnlyList<string> GetCompletedItems(string? instanceId, DateTime now)
    {
        var instanceKey = NormalizeInstanceKey(instanceId);
        var gameDay = GetGameDay(now);
        lock (SyncRoot)
        {
            return ReadRecords()
                .Where(record => string.Equals(record.GameDay, gameDay, StringComparison.Ordinal)
                    && string.Equals(record.InstanceKey, instanceKey, StringComparison.Ordinal))
                .Select(record => NormalizeItem(record.Item))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
    }

    /// <summary>将指定实例的日课项目记录为当前游戏日已完成。</summary>
    public static void MarkCompleted(string? instanceId, string item, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(item))
            throw new ArgumentException("日课项目标识不能为空。", nameof(item));

        var instanceKey = NormalizeInstanceKey(instanceId);
        var canonicalItem = NormalizeItem(item);
        var gameDay = GetGameDay(now);
        lock (SyncRoot)
        {
            if (CountRecords(ReadRecords(), instanceKey, canonicalItem, gameDay) > 0)
                return;

            AppendRecord(gameDay, instanceKey, canonicalItem);
        }

        CompletionChanged?.Invoke(null, instanceKey);
    }

    /// <summary>
    /// 把指定实例的日课项目标记为本次运行跳过。
    /// 只影响该实例当前这一次任务运行，不写入完成记录，设置页与下次运行都不受影响。
    /// </summary>
    public static void MarkSkippedForCurrentRun(string? instanceId, string item)
    {
        if (string.IsNullOrWhiteSpace(item))
            throw new ArgumentException("日课项目标识不能为空。", nameof(item));

        var instanceKey = NormalizeInstanceKey(instanceId);
        lock (RunStateLock)
        {
            if (!RunSkippedItems.TryGetValue(instanceKey, out var skippedItems))
            {
                skippedItems = new HashSet<string>(StringComparer.Ordinal);
                RunSkippedItems[instanceKey] = skippedItems;
            }

            skippedItems.Add(NormalizeItem(item));
        }
    }

    /// <summary>判断指定实例的日课项目是否已在本次运行中被跳过。</summary>
    public static bool IsSkippedForCurrentRun(string? instanceId, string item)
    {
        if (string.IsNullOrWhiteSpace(item))
            return false;

        var instanceKey = NormalizeInstanceKey(instanceId);
        lock (RunStateLock)
        {
            return RunSkippedItems.TryGetValue(instanceKey, out var skippedItems)
                && skippedItems.Contains(NormalizeItem(item));
        }
    }

    /// <summary>清空指定实例本次运行的跳过状态，由该实例日课入口的动作调用。</summary>
    public static void ClearRunSkips(string? instanceId)
    {
        var instanceKey = NormalizeInstanceKey(instanceId);
        lock (RunStateLock)
            RunSkippedItems.Remove(instanceKey);
    }

    private static string GetLogPath()
    {
        return Path.Combine(AppPaths.LogsDirectory, CompletionLogFileName);
    }

    private static IReadOnlyList<CompletionRecord> ReadRecords()
    {
        var path = GetLogPath();
        if (!File.Exists(path))
            return [];

        var records = new List<CompletionRecord>();
        foreach (var line in File.ReadLines(path))
        {
            // 台账每行为「游戏日 + 实例 + 项目」三列，旧版为「游戏日 + 项目」两列。
            var fields = line.Split('\t', StringSplitOptions.TrimEntries);
            if (fields.Length == 3
                && !string.IsNullOrWhiteSpace(fields[0])
                && !string.IsNullOrWhiteSpace(fields[1])
                && !string.IsNullOrWhiteSpace(fields[2]))
            {
                records.Add(new CompletionRecord(fields[0], fields[1], fields[2]));
            }
            else if (fields.Length == 2
                && !string.IsNullOrWhiteSpace(fields[0])
                && !string.IsNullOrWhiteSpace(fields[1]))
            {
                records.Add(new CompletionRecord(fields[0], LegacyInstanceKey, fields[1]));
            }
        }

        return records;
    }

    private static string GetGameDay(DateTime now)
    {
        var localTime = now.Kind == DateTimeKind.Utc ? now.ToLocalTime() : now;
        if (localTime.TimeOfDay < ResetTime.ToTimeSpan())
            localTime = localTime.AddDays(-1);

        return DateOnly.FromDateTime(localTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string NormalizeItem(string item)
    {
        return LegacyItemAliases.TryGetValue(item, out var canonicalItem)
            ? canonicalItem
            : item;
    }

    private static int CountRecords(
        IReadOnlyList<CompletionRecord> records,
        string instanceKey,
        string canonicalItem,
        string gameDay)
    {
        return records.Count(record =>
            string.Equals(record.GameDay, gameDay, StringComparison.Ordinal)
            && string.Equals(record.InstanceKey, instanceKey, StringComparison.Ordinal)
            && string.Equals(NormalizeItem(record.Item), canonicalItem, StringComparison.Ordinal));
    }

    private static void AppendRecord(string gameDay, string instanceKey, string canonicalItem)
    {
        Directory.CreateDirectory(AppPaths.LogsDirectory);
        File.AppendAllText(
            GetLogPath(),
            $"{gameDay}\t{instanceKey}\t{canonicalItem}{Environment.NewLine}");
    }

    private sealed record CompletionRecord(string GameDay, string InstanceKey, string Item);
}
