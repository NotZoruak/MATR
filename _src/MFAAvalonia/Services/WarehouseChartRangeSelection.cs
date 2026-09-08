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
    public string SummaryText => $"起始：{Start.RecordedAt:yyyy-MM-dd HH:mm:ss} · 终止：{End.RecordedAt:yyyy-MM-dd HH:mm:ss}\n相隔：{FormatDuration(Duration)} · 变化：{Change:+#,##0;-#,##0;0}";

    private static string FormatDuration(TimeSpan duration)
    {
        var totalHours = (int)duration.TotalHours;
        return $"{totalHours}小时{duration.Minutes}分钟";
    }
}
