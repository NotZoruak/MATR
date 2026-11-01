using System;
using System.Collections.Generic;
using System.Linq;

namespace MFAAvalonia.Services;

/// <summary>核心资源图表的时间轴刻度。</summary>
public sealed class WarehouseChartTimeAxisLabel
{
    public WarehouseChartTimeAxisLabel(double x, string text, double y = 0, double fontSize = 10)
    {
        X = x;
        Text = text;
        Y = y;
        FontSize = fontSize;
    }

    public double X { get; }
    public string Text { get; }
    public double Y { get; }
    public double FontSize { get; }
}

/// <summary>生成核心资源图表的时间轴。</summary>
public static class WarehouseChartTimeAxis
{
    public static IReadOnlyList<WarehouseChartTimeAxisLabel> BuildLabels(
        DateTime start,
        DateTime end,
        double width)
    {
        var labelCount = Math.Max(5, (int)(width / 160) + 1);
        var labels = new List<WarehouseChartTimeAxisLabel>(labelCount);
        var elapsed = end - start;
        var format = elapsed <= TimeSpan.FromDays(1)
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

    public static IReadOnlyList<WarehouseChartTimeAxisLabel> BuildVisibleLabels(
        DateTime start,
        DateTime end,
        double canvasWidth,
        double viewportWidth,
        double scrollOffset,
        double labelY = 0,
        double fontSize = 10)
    {
        return BuildLabels(start, end, canvasWidth)
            .Where(label => label.X >= scrollOffset - 40 && label.X <= scrollOffset + viewportWidth + 40)
            .Select(label => new WarehouseChartTimeAxisLabel(label.X - scrollOffset, label.Text, labelY, fontSize))
            .ToList();
    }
}
