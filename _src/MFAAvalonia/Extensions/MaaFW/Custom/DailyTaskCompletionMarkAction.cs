using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Helper;
using MFAAvalonia.Services;
using System;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>记录一次日课项目完成进度，达到要求次数后不再重复写入。</summary>
public sealed class DailyTaskCompletionMarkAction : IMaaCustomAction
{
    public string Name { get; set; } = nameof(DailyTaskCompletionMarkAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            ActionParamHelper.ThrowIfStopping(context);
            var param = ActionParamHelper.Parse(args.ActionParam);
            var item = param["item"]?.ToObject<string>();
            var requiredCount = param["required_count"]?.ToObject<int>() ?? 1;
            if (string.IsNullOrWhiteSpace(item))
            {
                LoggerHelper.Error("[日课] 未提供进度记录的项目标识");
                return false;
            }

            if (requiredCount <= 0)
            {
                LoggerHelper.Error($"[日课] 项目={item} 的完成次数要求必须大于零");
                return false;
            }

            var instanceId = ActionParamHelper.ResolveOwnerInstanceId(context);
            var completedCount = DailyTaskCompletionService.RecordProgress(
                instanceId,
                item,
                requiredCount,
                DateTime.Now);
            LoggerHelper.Info($"[日课] 记录进度：实例={DailyTaskCompletionService.NormalizeInstanceKey(instanceId)}，项目={item}，次数={completedCount}/{requiredCount}");
            return true;
        }
        catch (MaaStopException)
        {
            return false;
        }
        catch (Exception exception)
        {
            LoggerHelper.Error($"[日课] 记录项目进度失败：{exception.Message}");
            return false;
        }
    }
}
