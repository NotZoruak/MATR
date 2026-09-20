using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Services;
using System;
using System.Linq;

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
            var param = ActionParamHelper.Parse(args.ActionParam);
            var instanceId = param["instance_id"]?.ToObject<string>();
            var configuration = !string.IsNullOrWhiteSpace(instanceId)
                ? MaaProcessor.Processors.FirstOrDefault(processor => processor.InstanceId == instanceId)?.InstanceConfiguration
                    ?? new InstanceConfiguration(instanceId)
                : ActionParamHelper.ResolveOwnerProcessor(context)?.InstanceConfiguration
                    ?? ConfigurationManager.CurrentInstance;
            var scheduleKey = UpdateDataScheduleService.NormalizeKey(param["key"]?.ToObject<string>());
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
