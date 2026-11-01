using System;

namespace MFAAvalonia.Services;

public sealed class WarehouseChartRangeSelection
{
    public WarehouseChartSelectionPoint? Start { get; private set; }
    public WarehouseChartSelectionPoint? End { get; private set; }

    public WarehouseChartRangeSelectionResult? Select(DateTime recordedAt, int value)
    {
        if (End != null)
        {
            Start = null;
            End = null;
        }

        var point = new WarehouseChartSelectionPoint(recordedAt, value);
        if (Start == null)
        {
            Start = point;
            return null;
        }

        if (point.RecordedAt < Start.RecordedAt)
        {
            End = Start;
            Start = point;
        }
        else
        {
            End = point;
        }

        return new WarehouseChartRangeSelectionResult(Start, End);
    }

    public void Reset()
    {
        Start = null;
        End = null;
    }
}

public sealed record WarehouseChartSelectionPoint(DateTime RecordedAt, int Value);

public sealed record WarehouseChartRangeSelectionResult(
    WarehouseChartSelectionPoint Start,
    WarehouseChartSelectionPoint End)
{
    public TimeSpan Duration => End.RecordedAt - Start.RecordedAt;
    public int Change => End.Value - Start.Value;
    public string SummaryText => $"起始：{Start.RecordedAt:yyyy-MM-dd HH:mm:ss} · 终止：{End.RecordedAt:yyyy-MM-dd HH:mm:ss}\n相隔：{FormatDuration(Duration)} · 变化：{Change:+#,##0;-#,##0;0}（平均变化速率：{FormatAverageRate(Change, Duration)}）";

    public static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalDays >= 1
            ? $"{duration.Days}天{duration.Hours}小时{duration.Minutes}分钟"
            : $"{(int)duration.TotalHours}小时{duration.Minutes}分钟";
    }

    public static string FormatAverageRate(int change, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            return "无法计算";

        var isDailyRate = duration.TotalDays >= 1;
        var rate = isDailyRate ? change / duration.TotalDays : change / duration.TotalHours;
        var unit = isDailyRate ? "/天" : "/小时";
        return $"{rate:+0.00;-0.00;0.00}{unit}";
    }
}
