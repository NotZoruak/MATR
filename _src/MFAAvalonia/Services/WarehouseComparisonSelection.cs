using System;

namespace MFAAvalonia.Services;

/// <summary>管理对比图中跨资源共享的时间点选择。</summary>
public sealed class WarehouseComparisonSelection
{
    public DateTime? Start { get; private set; }
    public DateTime? End { get; private set; }
    public TimeSpan? Duration => Start.HasValue && End.HasValue ? End - Start : null;

    /// <summary>选择一个时间点；完成范围后再次点击会开始新的范围。</summary>
    public bool Select(DateTime recordedAt)
    {
        if (End != null)
        {
            Start = recordedAt;
            End = null;
            return false;
        }

        if (Start == null)
        {
            Start = recordedAt;
            return false;
        }

        if (recordedAt < Start.Value)
        {
            End = Start;
            Start = recordedAt;
        }
        else
        {
            End = recordedAt;
        }

        return true;
    }
}
