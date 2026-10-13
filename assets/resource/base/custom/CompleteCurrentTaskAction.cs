using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using System.Linq;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>
/// 提前结束当前队列项，或在当前队列项结束后停止整个任务队列。
/// </summary>
public class CompleteCurrentTaskAction : IMaaCustomAction
{
    public string Name { get; set; } = nameof(CompleteCurrentTaskAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        var processor = MaaProcessor.Processors.FirstOrDefault(item => ReferenceEquals(item.MaaTasker, context.Tasker));
        if (processor == null)
        {
            LoggerHelper.Warning("[提前结束任务] 未找到当前任务处理器");
            return false;
        }

        var parameters = ActionParamHelper.Parse(args.ActionParam);
        var reason = (string?)parameters["reason"];
        var stopQueue = bool.TryParse(parameters["stop_queue"]?.ToString(), out var stopQueueValue)
            && stopQueueValue;
        var requestAccepted = stopQueue
            ? processor.RequestStopAfterActiveTask(reason)
            : processor.RequestEarlyCompletionForActiveTask(reason);
        if (!requestAccepted)
        {
            LoggerHelper.Warning(stopQueue
                ? "[提前结束任务] 当前没有可停止的队列项"
                : "[提前结束任务] 当前没有可结束的队列项");
            return false;
        }

        return true;
    }
}
