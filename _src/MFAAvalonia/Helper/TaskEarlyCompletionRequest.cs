using System.Threading;

namespace MFAAvalonia.Helper;

/// <summary>
/// 保存某个队列项的一次性提前结束请求。
/// </summary>
public sealed class TaskEarlyCompletionRequest
{
    private string? _reason;

    /// <summary>
    /// 请求提前结束。首次请求成功，后续请求保留首次原因。
    /// </summary>
    public bool Request(string? reason)
    {
        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? "提前结束" : reason.Trim();
        return Interlocked.CompareExchange(ref _reason, normalizedReason, null) == null;
    }

    /// <summary>
    /// 消费提前结束请求。请求只能被消费一次。
    /// </summary>
    public bool TryConsume(out string reason)
    {
        var requestedReason = Interlocked.Exchange(ref _reason, null);
        if (requestedReason == null)
        {
            reason = string.Empty;
            return false;
        }

        reason = requestedReason;
        return true;
    }
}
