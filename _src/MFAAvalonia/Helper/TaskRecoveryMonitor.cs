using System;

namespace MFAAvalonia.Helper;

/// <summary>独立于底层任务等待，记录回调静默和重复动作；每个执行器独立持有。</summary>
public sealed class TaskRecoveryMonitor
{
    /// <summary>模拟器无响应（无回调）类原因的固定前缀，供恢复流程区分恢复力度。</summary>
    public const string EmulatorUnresponsiveReasonPrefix = "模拟器无响应";

    private readonly object _gate = new();
    private readonly LoopDetector _loopDetector = new();
    private TimeSpan _lastCallback;
    private bool _active;
    private string? _loopReason;

    public void Start(TimeSpan now)
    {
        lock (_gate)
        {
            _active = true;
            _lastCallback = now;
            _loopReason = null;
            _loopDetector.Reset();
        }
    }

    public void Stop()
    {
        lock (_gate) _active = false;
    }

    public void RecordCallback(TimeSpan now)
    {
        lock (_gate)
        {
            if (_active) _lastCallback = now;
        }
    }

    public void FeedAction(string name, string action, int x, int y)
    {
        lock (_gate)
        {
            // 只累计真正的点击动作，避免等待、识别和自定义动作被当作冻结画面。
            if (_active && action == "Click" && !string.IsNullOrWhiteSpace(name)
                && _loopDetector.Feed(name, action, x, y))
                _loopReason = $"画面冻结：重复点击 {name}，坐标 ({x},{y})";
        }
    }

    public string? GetReason(TimeSpan now, TimeSpan timeout, bool enabled, bool waiting)
    {
        lock (_gate)
        {
            if (!_active) return null;
            if (!enabled || waiting)
            {
                _lastCallback = now;
                _loopReason = null;
                _loopDetector.Reset();
                return null;
            }
            return _loopReason ?? (now - _lastCallback >= timeout
                ? $"{EmulatorUnresponsiveReasonPrefix}：超过 {timeout.TotalSeconds:0} 秒没有任务回调"
                : null);
        }
    }

    /// <summary>
    /// 该原因是否属于模拟器整机无响应。只有这类形态需要强制重启模拟器；
    /// 画面冻结（动作循环）时模拟器仍在响应，应先只重启游戏。
    /// </summary>
    public static bool IsEmulatorUnresponsiveReason(string? reason) =>
        reason != null && reason.StartsWith(EmulatorUnresponsiveReasonPrefix, StringComparison.Ordinal);

    public static bool ShouldStartEmulator(bool retryLaunch, bool restartAdb, bool hardRestartAdb)
        => retryLaunch || restartAdb || hardRestartAdb;

    public static string GetDisplayReason(string reason)
    {
        if (reason.StartsWith("动作循环卡死", StringComparison.Ordinal))
            return $"动作循环{reason["动作循环卡死".Length..]}";
        if (reason.StartsWith("画面冻结", StringComparison.Ordinal))
            return $"动作循环{reason["画面冻结".Length..]}";
        return reason;
    }
}
