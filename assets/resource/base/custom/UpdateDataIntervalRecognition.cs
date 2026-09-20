using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Services;
using System;
using System.Linq;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>
/// 判断更新数据任务是否已达触发间隔。未到间隔时识别失败，
/// 由上级 node 的 next 落到跳过出口，本次运行不做任何操作。
/// </summary>
public sealed class UpdateDataIntervalRecognition : IMaaCustomRecognition
{
    public string Name { get; set; } = nameof(UpdateDataIntervalRecognition);

    public bool Analyze<T>(T context, in AnalyzeArgs args, in AnalyzeResults results) where T : IMaaContext
    {
        try
        {
            ActionParamHelper.ThrowIfStopping(context);
            var param = ActionParamHelper.Parse(args.RecognitionParam);
            var interval = param["interval"]?.ToObject<string>();
            if (string.IsNullOrWhiteSpace(interval))
            {
                LoggerHelper.Error("[更新数据] 未提供触发间隔");
                return true;
            }

            var scheduleKey = UpdateDataScheduleService.NormalizeKey(param["key"]?.ToObject<string>());
            var instanceId = param["instance_id"]?.ToObject<string>();
            // 优先使用任务参数中的实例标识，避免重启后无法从 MaaFramework context 反推出执行实例。
            var configuration = !string.IsNullOrWhiteSpace(instanceId)
                ? MaaProcessor.Processors.FirstOrDefault(processor => processor.InstanceId == instanceId)?.InstanceConfiguration
                    ?? new InstanceConfiguration(instanceId)
                : ActionParamHelper.ResolveOwnerProcessor(context)?.InstanceConfiguration
                    ?? ConfigurationManager.CurrentInstance;
            var lastSucceeded = UpdateDataScheduleService.GetLastSucceeded(configuration, scheduleKey);
            var lastSucceededText = lastSucceeded == null
                ? "无"
                : UpdateDataScheduleService.FormatLocalTime(lastSucceeded.Value);

            var shouldRun = UpdateDataScheduleService.ShouldRun(configuration, scheduleKey, interval, DateTime.Now);
            LoggerHelper.Info(
                $"[更新数据] 调度键={scheduleKey}，触发间隔={interval}，上次成功时间={lastSucceededText}，" +
                (shouldRun ? "本次执行" : "未到间隔，跳过本次运行"));
            return shouldRun;
        }
        catch (MaaStopException)
        {
            return false;
        }
        catch (Exception exception)
        {
            // 读取状态失败时按需要执行处理，避免因为判断异常漏掉本次数据更新
            LoggerHelper.Error($"[更新数据] 触发间隔判断失败，本次按执行处理：{exception.Message}");
            return true;
        }
    }
}
