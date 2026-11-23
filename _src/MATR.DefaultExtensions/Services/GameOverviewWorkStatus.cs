using System;
using System.Globalization;

namespace MFAAvalonia.Services;

/// <summary>按国服时区计算完成状态，显示时省略年份。</summary>
public static class GameOverviewWorkStatus
{
    public sealed record Display(string Text, bool Completed);

    /// <summary>远征期限已到仅改变显示，不篡改日志中的部队状态。</summary>
    public static Display FormatExpedition(GameOverviewLogParser.TeamEntry team, string text, DateTimeOffset now)
    {
        var returned = team.Status == "2" && Format(team.FinishedAt, now).Completed;
        return new Display(returned && text.StartsWith("远征中", StringComparison.Ordinal)
            ? "远征归来" + text[3..] : text, returned);
    }

    public static Display Format(string? finishedAt, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(finishedAt) || finishedAt == "0" || finishedAt.StartsWith("0000", StringComparison.Ordinal))
            return new Display("未使用", false);
        if (!DateTime.TryParseExact(finishedAt, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            return new Display("状态未知", false);
        var deadline = new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Unspecified), TimeSpan.FromHours(8));
        return deadline <= now
            ? new Display("完成", true)
            : new Display("结束时间：" + time.ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture), false);
    }

    public static Display FormatSchedule(string? finishedAt, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(finishedAt) || finishedAt == "0" || finishedAt.StartsWith("0000", StringComparison.Ordinal))
            return new Display("未使用", false);
        if (!DateTime.TryParseExact(finishedAt, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            return new Display("状态未知", false);
        var deadline = new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Unspecified), TimeSpan.FromHours(8));
        var endText = time.ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var remaining = deadline - now.ToOffset(TimeSpan.FromHours(8));
        if (remaining <= TimeSpan.Zero) return new Display("已完成", true);
        var countdown = $"{(long)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
        return new Display($"结束：{endText}，剩余：{countdown}", false);
    }
}
