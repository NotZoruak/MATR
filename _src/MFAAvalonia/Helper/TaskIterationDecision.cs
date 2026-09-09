namespace MFAAvalonia.Helper;

/// <summary>
/// 决定当前一轮流程结束后是否计入进度并继续重复。
/// </summary>
public readonly record struct TaskIterationDecision(
    bool ShouldCountIteration,
    bool ShouldContinueRepeating,
    string? Reason)
{
    /// <summary>
    /// 根据一次性提前结束请求生成当前轮的执行决策。
    /// </summary>
    public static TaskIterationDecision Resolve(TaskEarlyCompletionRequest earlyCompletionRequest)
    {
        if (earlyCompletionRequest.TryConsume(out var reason))
            return new TaskIterationDecision(false, false, reason);

        return new TaskIterationDecision(true, true, null);
    }
}
