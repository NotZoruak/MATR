using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using System.Linq;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>
/// 请求提前结束当前队列项的剩余重复次数。
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

        var reason = (string?)ActionParamHelper.Parse(args.ActionParam)["reason"];
        if (!processor.RequestEarlyCompletionForActiveTask(reason))
        {
            LoggerHelper.Warning("[提前结束任务] 当前没有可结束的队列项");
            return false;
        }

        return true;
    }
}
