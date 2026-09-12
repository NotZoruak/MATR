using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using System.Linq;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

public class StopOnDamageAction : IMaaCustomAction
{
    public string Name { get; set; } = nameof(StopOnDamageAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        var processor = ActionParamHelper.ResolveOwnerProcessor(context);
        if (processor == null)
        {
            Log(context, "无法获取当前任务处理器");
            return true;
        }

        var currentTaskName = processor.ViewModel?.CurrentTaskName ?? "未知任务";

        // 当前任务已被 Dequeue，队列头部即为下一任务（跳过回本丸等自动插入的中间任务）
        // ObservableQueue 不提供遍历，仅用 Any 判断是否存在下一个非回本丸任务
        var hasNextTask = processor.TaskQueue.Any(t => t.Name != "回本丸");

        var message = hasNextTask
            ? $"[重伤检测] 检测到刀剑男士重伤，{currentTaskName} 任务终止，开始下一任务"
            : $"[重伤检测] 检测到刀剑男士重伤，{currentTaskName} 任务终止，所有任务运行完毕";

        Log(context, message);

        // 系统托盘通知（使用应用内 Toast 弹窗）
        DispatcherHelper.PostOnMainThread(() =>
        {
            ToastHelper.Warn("重伤检测", message, duration: 10);
        });

        return true; // 流水线节点成功 → MaaTasker 任务结束 → 队列自动移至下一任务
    }

    private static void Log<T>(T context, string message) where T : IMaaContext
    {
        LoggerHelper.Info(message);
        try
        {
            ActionParamHelper.ResolveOwnerProcessor(context)?.AddLog(message);
        }
        catch
        {
            // 静默忽略，确保不影响流水线执行
        }
    }
}
