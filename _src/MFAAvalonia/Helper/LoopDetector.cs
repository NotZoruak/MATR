#nullable enable
using System;

namespace MFAAvalonia.Helper;

/// <summary>
/// 卡死循环检测器:统计连续相同的动作键(节点名+动作类型),
/// 连续达到阈值并通过二次确认后判定画面冻结。
/// 用于「卡死重启」开启时的补充检测,覆盖枢纽 timeout 检测不到的形态:
/// 冻结画面恰好命中某个识别节点,形成"识别命中→执行→跳回枢纽→timeout 重置"的无限循环。
/// </summary>
public sealed class LoopDetector
{
    /// <summary>连续相同动作次数阈值:画面冻结约 30-200 秒(按 350ms-2s 循环周期)</summary>
    public const int LoopThreshold = 80;

    /// <summary>达到阈值后的二次确认追加次数,确认期内动作键变化则放弃判定</summary>
    public const int ConfirmCount = 20;

    /// <summary>状态读写锁:Feed(回调线程)与 Reset(主线程)可能跨线程调用</summary>
    private readonly object _lock = new();
    private string? _lastKey;
    private int _count;
    private int _confirmCount;

    /// <summary>是否已判定卡死触发(触发后需调用 <see cref="Reset"/> 复位)</summary>
    public bool IsTriggered { get; private set; }

    /// <summary>
    /// 喂入一次成功执行的动作事件。
    /// </summary>
    /// <returns>是否达到触发条件</returns>
    public bool Feed(string nodeName, string action, int x, int y)
    {
        lock (_lock)
        {
            if (IsTriggered)
            {
                // 已触发静默期:不再累计计数也不重复触发,直至 Reset 复位
                return false;
            }

            // OCR 匹配框会因抖动产生数像素偏移，不能把其坐标作为循环中断条件。
            var key = $"{nodeName}|{action}";
            if (key != _lastKey)
            {
                // 动作键变化:画面已变化,计数清零
                _lastKey = key;
                _count = 1;
                _confirmCount = 0;
                return false;
            }

            _count++;
            if (_count < LoopThreshold)
                return false;

            // 二次确认阶段:再累计 ConfirmCount 次(期间键不变)才触发
            _confirmCount++;
            if (_confirmCount < ConfirmCount)
                return false;

            IsTriggered = true;
            return true;
        }
    }

    /// <summary>复位状态(任务停止、恢复流程开始时调用)</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _lastKey = null;
            _count = 0;
            _confirmCount = 0;
            IsTriggered = false;
        }
    }
}
