using MFAAvalonia.Helper;
using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;

namespace MFAAvalonia.Services;

/// <summary>管理日课项目按游戏日完成一次的持久化状态，以及本次运行期间的临时跳过状态。</summary>
public static class DailyTaskCompletionService
{
    private static readonly TimeOnly ResetTime = new(5, 0);
    private static readonly object SyncRoot = new();
    private const string CompletionLogFileName = "daily-task-completion.log";
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
    private static readonly HashSet<string> RunSkippedItems = new(StringComparer.Ordinal);

    /// <summary>当前游戏日完成记录发生变化时通知界面刷新。</summary>
    public static event EventHandler? CompletionChanged;

    /// <summary>判断指定日课项目在当前游戏日是否仍应执行。</summary>
    public static bool ShouldRun(string item, DateTime now)
    {
        return ShouldRun(item, 1, now);
    }

    /// <summary>判断指定日课项目在当前游戏日的完成次数是否尚未达到要求。</summary>
    public static bool ShouldRun(string item, int requiredCount, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(item))
            throw new ArgumentException("日课项目标识不能为空。", nameof(item));
        if (requiredCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(requiredCount), "完成次数必须大于零。");

        return GetCompletedCount(item, now) < requiredCount;
    }

    /// <summary>获取指定日课项目在当前游戏日的进度记录数。</summary>
    public static int GetCompletedCount(string item, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(item))
            throw new ArgumentException("日课项目标识不能为空。", nameof(item));

        var canonicalItem = NormalizeItem(item);
        var gameDay = GetGameDay(now);
        lock (SyncRoot)
        {
            return CountRecords(ReadRecords(), canonicalItem, gameDay);
        }
    }

    /// <summary>
    /// 追加一次指定日课项目的进度记录，返回写入后当前游戏日的记录数。
    /// 供锻刀、演练等按游戏日累计多次的项目使用，达到 requiredCount 后不再写入。
    /// </summary>
    public static int RecordProgress(string item, int requiredCount, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(item))
            throw new ArgumentException("日课项目标识不能为空。", nameof(item));
        if (requiredCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(requiredCount), "完成次数必须大于零。");

        var canonicalItem = NormalizeItem(item);
        var gameDay = GetGameDay(now);
        int completedCount;
        lock (SyncRoot)
        {
            var records = ReadRecords();
            completedCount = CountRecords(records, canonicalItem, gameDay);
            if (completedCount >= requiredCount)
                return completedCount;

            AppendRecord(gameDay, canonicalItem);
            completedCount++;
        }

        CompletionChanged?.Invoke(null, EventArgs.Empty);
        return completedCount;
    }

    /// <summary>获取当前游戏日已有记录的项目标识；未达到上限的进度项目同样会被返回。</summary>
    public static IReadOnlyList<string> GetCompletedItems(DateTime now)
    {
        var gameDay = GetGameDay(now);
        lock (SyncRoot)
        {
            return ReadRecords()
                .Where(record => string.Equals(record.GameDay, gameDay, StringComparison.Ordinal))
                .Select(record => NormalizeItem(record.Item))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
    }

    /// <summary>将指定日课项目记录为当前游戏日已完成。</summary>
    public static void MarkCompleted(string item, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(item))
            throw new ArgumentException("日课项目标识不能为空。", nameof(item));

        var canonicalItem = NormalizeItem(item);
        var gameDay = GetGameDay(now);
        lock (SyncRoot)
        {
            if (CountRecords(ReadRecords(), canonicalItem, gameDay) > 0)
                return;

            AppendRecord(gameDay, canonicalItem);
        }

        CompletionChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// 把指定日课项目标记为本次运行跳过。
    /// 只影响当前这一次任务运行，不写入完成记录，设置页与下次运行都不受影响。
    /// </summary>
    public static void MarkSkippedForCurrentRun(string item)
    {
        if (string.IsNullOrWhiteSpace(item))
            throw new ArgumentException("日课项目标识不能为空。", nameof(item));

        lock (RunStateLock)
            RunSkippedItems.Add(NormalizeItem(item));
    }

    /// <summary>判断指定日课项目是否已在本次运行中被跳过。</summary>
    public static bool IsSkippedForCurrentRun(string item)
    {
        if (string.IsNullOrWhiteSpace(item))
            return false;

        lock (RunStateLock)
            return RunSkippedItems.Contains(NormalizeItem(item));
    }

    /// <summary>清空本次运行的跳过状态，由每次任务开始时的入口动作调用。</summary>
    public static void ClearRunSkips()
    {
        lock (RunStateLock)
            RunSkippedItems.Clear();
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
            var fields = line.Split('\t', 2, StringSplitOptions.TrimEntries);
            if (fields.Length == 2
                && !string.IsNullOrWhiteSpace(fields[0])
                && !string.IsNullOrWhiteSpace(fields[1]))
                records.Add(new CompletionRecord(fields[0], fields[1]));
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

    private static int CountRecords(IReadOnlyList<CompletionRecord> records, string canonicalItem, string gameDay)
    {
        return records.Count(record =>
            string.Equals(record.GameDay, gameDay, StringComparison.Ordinal)
            && string.Equals(NormalizeItem(record.Item), canonicalItem, StringComparison.Ordinal));
    }

    private static void AppendRecord(string gameDay, string canonicalItem)
    {
        Directory.CreateDirectory(AppPaths.LogsDirectory);
        File.AppendAllText(
            GetLogPath(),
            $"{gameDay}\t{canonicalItem}{Environment.NewLine}");
    }

    private sealed record CompletionRecord(string GameDay, string Item);
}
