using MFAAvalonia.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MFAAvalonia.Services;

/// <summary>核心资源图表的时间范围。</summary>
public enum WarehouseChartRange
{
    Last24Hours,
    Last7Days,
    Last30Days,
    All,
}

/// <summary>筛选核心资源历史记录。</summary>
public static class WarehouseResourceHistoryFilter
{
    public static IReadOnlyList<WarehouseResourceSnapshot> Filter(
        IEnumerable<WarehouseResourceSnapshot> history,
        WarehouseChartRange range,
        DateTime now)
    {
        return FilterWithIndices(history, range, now)
            .Select(item => item.Snapshot)
            .ToList();
    }

    public static IReadOnlyList<(WarehouseResourceSnapshot Snapshot, int Index)> FilterWithIndices(
        IEnumerable<WarehouseResourceSnapshot> history,
        WarehouseChartRange range,
        DateTime now)
    {
        if (range == WarehouseChartRange.All)
        {
            return history
                .Select((snapshot, index) => (Snapshot: snapshot, Index: index))
                .ToList();
        }

        var cutoff = now - range switch
        {
            WarehouseChartRange.Last24Hours => TimeSpan.FromHours(24),
            WarehouseChartRange.Last7Days => TimeSpan.FromDays(7),
            WarehouseChartRange.Last30Days => TimeSpan.FromDays(30),
            _ => throw new ArgumentOutOfRangeException(nameof(range), range, "不支持的图表时间范围"),
        };

        return history
            .Select((snapshot, index) => (Snapshot: snapshot, Index: index))
            .Where(item => item.Snapshot.RecordedAt >= cutoff && item.Snapshot.RecordedAt <= now)
            .ToList();
    }
}
