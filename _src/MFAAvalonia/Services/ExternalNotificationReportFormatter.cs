using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MFAAvalonia.Services;

/// <summary>将外部通知运行摘要格式化为纯文本结束报告。</summary>
public static class ExternalNotificationReportFormatter
{
    private static readonly string[] SwordTypeOrder = ["短刀", "胁差", "打刀", "太刀", "大太刀", "枪", "薙刀", "剑"];

    public static string Format(ExternalNotificationRunSession session, ExternalNotificationReportOptions options)
    {
        var builder = new StringBuilder();
        var hasFailures = session.Segments.Any(segment => segment.Summary.FailedTasks.Count > 0);
        builder.AppendLine(hasFailures ? "任务执行结束（存在失败）" : "任务已全部完成");
        builder.Append("总用时：").AppendLine(FormatDuration(session.CompletedAt - session.StartedAt));

        var showInstanceName = session.Segments.Count > 1;
        foreach (var segment in session.Segments)
        {
            var summary = segment.Summary;
            if (showInstanceName)
                builder.AppendLine().AppendLine(summary.InstanceName);

            foreach (var task in summary.Tasks.Values)
                builder.Append(task.TaskName).Append(" ×").Append(task.RepeatCount).Append("：").AppendLine(FormatDuration(task.Elapsed));

            if (summary.FailedTasks.Count > 0)
            {
                builder.AppendLine().AppendLine("失败任务");
                foreach (var task in summary.FailedTasks)
                {
                    builder.Append(task.TaskName).Append(" ×").Append(task.RepeatCount).Append("：").Append(FormatDuration(task.Elapsed));
                    if (!string.IsNullOrWhiteSpace(task.ErrorMessage))
                        builder.Append("，原因：").Append(task.ErrorMessage);
                    builder.AppendLine();
                }
            }

            if (options.IncludeTaskHarvest)
                AppendHarvest(builder, summary);
            if (options.IncludeLogistics)
                AppendLogistics(builder, summary);
            if (options.IncludeSpecialCases)
                AppendSpecialCases(builder, summary);
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendHarvest(StringBuilder builder, ExternalNotificationRunSummary summary)
    {
        if (summary.Resources.Count == 0 && summary.SwordDrops.Count == 0)
            return;

        builder.AppendLine().AppendLine("任务收获");
        if (summary.Resources.Count > 0)
            builder.Append("资源：").AppendLine(string.Join("，", summary.Resources.Select(pair => $"{pair.Key}×{pair.Value}")));

        var orderedTypes = summary.SwordDrops.Keys
            .OrderBy(type => Array.IndexOf(SwordTypeOrder, type) is var index && index >= 0 ? index : int.MaxValue)
            .ThenBy(type => type, StringComparer.Ordinal);
        foreach (var type in orderedTypes)
            builder.Append(type).Append("：").AppendLine(string.Join("，", summary.SwordDrops[type].Select(pair => $"{pair.Key}×{pair.Value}")));
    }

    private static void AppendLogistics(StringBuilder builder, ExternalNotificationRunSummary summary)
    {
        if (summary.LogisticsCounts.Count == 0 && summary.ExpeditionMaps.Count == 0 && summary.NaibanDetails.Count == 0 && summary.LogisticsRepairDetails.Count == 0)
            return;

        builder.AppendLine().AppendLine("后勤动态");
        if (summary.LogisticsCounts.Count > 0)
            builder.AppendLine(string.Join("　", summary.LogisticsCounts.Select(pair => $"{pair.Key} ×{pair.Value}")));
        if (summary.ExpeditionMaps.Count > 0)
        {
            builder.AppendLine($"派遣远征 ×{summary.ExpeditionMaps.Values.Sum()}");
            builder.AppendLine(string.Join("　", summary.ExpeditionMaps.Select(pair => $"{pair.Key} ×{pair.Value}")));
        }
        AppendDetails(builder, "内番", summary.NaibanDetails);
        AppendDetails(builder, "修刀", summary.LogisticsRepairDetails);
    }

    private static void AppendSpecialCases(StringBuilder builder, ExternalNotificationRunSummary summary)
    {
        if (summary.SpecialCases.Count == 0)
            return;

        builder.AppendLine().AppendLine("特殊情况");
        builder.AppendLine(string.Join("　", summary.SpecialCases.Select(pair => $"{pair.Key} ×{pair.Value}")));
    }

    private static void AppendDetails(StringBuilder builder, string title, IReadOnlyList<string> details)
    {
        if (details.Count == 0)
            return;

        builder.AppendLine(title);
        foreach (var detail in details)
            builder.AppendLine(detail);
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
            duration = TimeSpan.Zero;

        var parts = new List<string>();
        if (duration.TotalHours >= 1)
            parts.Add($"{(int)duration.TotalHours}小时");
        if (duration.Minutes > 0)
            parts.Add($"{duration.Minutes}分");
        if (parts.Count == 0)
            parts.Add($"{Math.Max(0, duration.Seconds)}秒");
        return string.Join(string.Empty, parts);
    }
}
