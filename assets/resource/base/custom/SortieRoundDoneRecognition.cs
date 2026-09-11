using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Helper;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>
/// 常驻作战「过去」模式的单圈结算识别。
///
/// 判定依据是出阵打点所在 node 的命中计数：本次任务运行内已经出阵过，
/// 说明这一圈已经打完并回到本丸，返回 true 由 pipeline 结束本轮任务运行；
/// 否则返回 false，继续按正常流程导航去出阵。
/// </summary>
public class SortieRoundDoneRecognition : IMaaCustomRecognition
{
    /// <summary>出阵打点所在的 node，命中一次代表一次出阵。</summary>
    private const string SortieSuccessNode = "S_SortieSuccess";

    /// <summary>按实例保存「任务运行标识 + 本轮出阵计数基线」。</summary>
    private static readonly ConcurrentDictionary<string, RunState> States = new(StringComparer.Ordinal);

    /// <summary>读取失败只提示一次，避免每圈刷屏。</summary>
    private static int _warningLogged;

    public string Name { get; set; } = nameof(SortieRoundDoneRecognition);

    public bool Analyze<T>(T context, in AnalyzeArgs args, in AnalyzeResults results) where T : IMaaContext
    {
        try
        {
            if (!context.GetHitCount(SortieSuccessNode, out var hitCount))
            {
                WarnOnce($"[常驻作战] 读取 {SortieSuccessNode} 命中计数失败，本轮结算按未出阵处理");
                return false;
            }

            var state = ResolveState(context);
            var jobId = context.TaskJob is { } job ? job.Id : 0L;

            // 任务运行标识变化说明队列开启了新一轮任务运行，需要重建出阵计数基线；
            // 命中计数回退（按轮归零等情况）同样视为新一轮，避免沿用旧基线。
            if (state.JobId != jobId || hitCount < state.Baseline)
            {
                state.JobId = jobId;
                state.Baseline = hitCount;
                return false;
            }

            return hitCount > state.Baseline;
        }
        catch (Exception e)
        {
            WarnOnce($"[常驻作战] 单圈结算识别异常，本轮按未出阵处理：{e.Message}");
            return false;
        }
    }

    /// <summary>按所属处理器实例取状态，避免多实例共用同一份计数。</summary>
    private static RunState ResolveState<T>(T context) where T : IMaaContext
    {
        var processor = MaaProcessor.Processors
            .FirstOrDefault(item => ReferenceEquals(item.MaaTasker, context.Tasker));
        var key = processor?.InstanceId ?? string.Empty;
        return States.GetOrAdd(key, _ => new RunState());
    }

    private static void WarnOnce(string message)
    {
        if (Interlocked.Exchange(ref _warningLogged, 1) == 0)
            LoggerHelper.Warning(message);
    }

    /// <summary>单个任务器实例的轮次状态。</summary>
    private sealed class RunState
    {
        /// <summary>最近一次任务运行标识。</summary>
        public long JobId { get; set; } = long.MinValue;

        /// <summary>本次任务运行开始时的出阵命中计数。</summary>
        public ulong Baseline { get; set; }
    }
}
