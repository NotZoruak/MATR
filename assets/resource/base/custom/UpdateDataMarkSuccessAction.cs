using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Services;
using System;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>记录更新数据任务最后一次完整成功时间。</summary>
public sealed class UpdateDataMarkSuccessAction : IMaaCustomAction
{
    public string Name { get; set; } = nameof(UpdateDataMarkSuccessAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            ActionParamHelper.ThrowIfStopping(context);
            // 多实例下「当前激活实例」可能不是执行任务的实例，成功时间必须写到真正执行任务的实例，
            // 否则设置页显示的上次更新时间会落到其它实例上。
            var configuration = ActionParamHelper.ResolveOwnerProcessor(context)?.InstanceConfiguration
                ?? ConfigurationManager.CurrentInstance;
            var scheduleKey = UpdateDataScheduleService.NormalizeKey(
                ActionParamHelper.Parse(args.ActionParam)["key"]?.ToObject<string>());
            UpdateDataScheduleService.MarkSucceeded(configuration, scheduleKey, DateTime.Now);
            LoggerHelper.Info($"[更新数据] 任务完成，已记录本次成功时间，调度键={scheduleKey}");
            return true;
        }
        catch (MaaStopException)
        {
            return false;
        }
        catch (Exception exception)
        {
            LoggerHelper.Error($"[更新数据] 记录成功时间失败：{exception.Message}");
            return false;
        }
    }
}
