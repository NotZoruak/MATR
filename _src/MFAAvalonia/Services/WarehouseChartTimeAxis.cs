using System;
using System.Collections.Generic;

namespace MFAAvalonia.Services;

/// <summary>核心资源图表的时间轴刻度。</summary>
public sealed class WarehouseChartTimeAxisLabel
{
    public WarehouseChartTimeAxisLabel(double x, string text)
    {
        X = x;
        Text = text;
    }

    public double X { get; }
    public string Text { get; }
}

/// <summary>生成核心资源图表的时间轴。</summary>
public static class WarehouseChartTimeAxis
{
    public static IReadOnlyList<WarehouseChartTimeAxisLabel> BuildLabels(
        DateTime start,
        DateTime end,
        WarehouseChartRange range,
        double width)
    {
        const int labelCount = 5;
        var labels = new List<WarehouseChartTimeAxisLabel>(labelCount);
        var elapsed = end - start;
        var format = range == WarehouseChartRange.Last24Hours
            || range == WarehouseChartRange.All && elapsed <= TimeSpan.FromDays(1)
            ? "HH:mm"
            : "MM-dd";
        for (var index = 0; index < labelCount; index++)
        {
            var ratio = (double)index / (labelCount - 1);
            labels.Add(new WarehouseChartTimeAxisLabel(
                width * ratio,
                start.AddTicks((long)(elapsed.Ticks * ratio)).ToString(format)));
        }

        return labels;
    }
}
