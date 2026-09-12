using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MaaFramework.Binding;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>
/// 解析 MaaFramework 传递的 custom_action_param 字符串。
/// MaaFramework 可能将 JObject 序列化为双重编码的字符串（如 "{\"seconds\":60}"），
/// 此工具类会自动处理这种情况。
/// </summary>
public static class ActionParamHelper
{
    /// <summary>
    /// 取当前任务所属的处理器。多实例下「当前激活实例」可能不是执行任务的实例，
    /// 日志与工作记录必须写到真正执行任务的实例，取不到时退回当前激活实例。
    /// </summary>
    public static MaaProcessor? ResolveOwnerProcessor(IMaaContext? context)
        => MaaProcessor.ResolveByTasker(context?.Tasker) ?? MaaProcessorManager.Instance?.Current;

    public static JObject Parse(string actionParam)
    {
        if (string.IsNullOrWhiteSpace(actionParam))
            return new JObject();

        // 先尝试直接解析为 JObject
        try
        {
            return JObject.Parse(actionParam);
        }
        catch
        {
            // 如果失败，可能是双重编码的字符串，先反序列化外层字符串
            try
            {
                var unwrapped = JsonConvert.DeserializeObject<string>(actionParam);
                if (!string.IsNullOrWhiteSpace(unwrapped))
                    return JObject.Parse(unwrapped);
            }
            catch
            {
                // ignored
            }
        }

        return new JObject();
    }

    public static void ThrowIfStopping(IMaaContext context)
    {
        if (context.Tasker.IsStopping)
        {
            throw new MaaStopException();
        }
    }

    public static void SleepWithStopCheck(IMaaContext context, int totalMilliseconds, int sliceMilliseconds = 200)
    {
        if (totalMilliseconds <= 0)
        {
            ThrowIfStopping(context);
            return;
        }

        var remaining = totalMilliseconds;
        while (remaining > 0)
        {
            ThrowIfStopping(context);

            var sleep = Math.Min(remaining, sliceMilliseconds);
            Thread.Sleep(sleep);
            remaining -= sleep;
        }
    }

    public static HttpResponseMessage SendHttpWithStopCheck(
        IMaaContext context,
        Func<CancellationToken, Task<HttpResponseMessage>> sendAsync)
    {
        ThrowIfStopping(context);

        using var cts = new CancellationTokenSource();
        using var monitor = Task.Run(async () =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    if (context.Tasker.IsStopping)
                    {
                        cts.Cancel();
                        break;
                    }

                    await Task.Delay(200, cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
        });

        try
        {
            return sendAsync(cts.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (context.Tasker.IsStopping)
        {
            throw new MaaStopException();
        }
        catch (TaskCanceledException) when (context.Tasker.IsStopping)
        {
            throw new MaaStopException();
        }
    }
}
