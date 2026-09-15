using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Helper;
using MFAAvalonia.Services;
using System;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>判断日课项目在当前游戏日是否仍需要执行。</summary>
public sealed class DailyTaskStepRecognition : IMaaCustomRecognition
{
    public string Name { get; set; } = nameof(DailyTaskStepRecognition);

    public bool Analyze<T>(T context, in AnalyzeArgs args, in AnalyzeResults results) where T : IMaaContext
    {
        try
        {
            ActionParamHelper.ThrowIfStopping(context);
            var param = ActionParamHelper.Parse(args.RecognitionParam);
            var item = param["item"]?.ToObject<string>();
            var requiredCount = param["required_count"]?.ToObject<int>() ?? 1;
            if (string.IsNullOrWhiteSpace(item))
            {
                LoggerHelper.Error("[日课] 未提供 Step 判断的项目标识");
                return false;
            }

            if (requiredCount <= 0)
            {
                LoggerHelper.Error($"[日课] 项目={item} 的完成次数要求必须大于零");
                return false;
            }

            if (DailyTaskCompletionService.IsSkippedForCurrentRun(item))
            {
                LoggerHelper.Info($"[日课] Step 项目={item}，本次运行已跳过");
                return false;
            }

            var shouldRun = DailyTaskCompletionService.ShouldRun(item, requiredCount, DateTime.Now);
            LoggerHelper.Info($"[日课] Step 项目={item}，要求次数={requiredCount}，当前游戏日{(shouldRun ? "未完成" : "已完成")}");
            return shouldRun;
        }
        catch (MaaStopException)
        {
            return false;
        }
        catch (Exception exception)
        {
            LoggerHelper.Error($"[日课] Step 判断失败：{exception.Message}");
            return false;
        }
    }
}
