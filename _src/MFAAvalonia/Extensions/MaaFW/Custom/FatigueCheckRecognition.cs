using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Helper;
using System;
using System.Linq;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>判断队伍疲劳状态是否满足当前流程条件。</summary>
public sealed class FatigueCheckRecognition : IMaaCustomRecognition
{
    public string Name { get; set; } = nameof(FatigueCheckRecognition);

    /// <summary>仅当至少有一个已识别疲劳值低于阈值时返回 true。</summary>
    public static bool ShouldBrush(int?[] fatigueValues, int threshold)
        => fatigueValues.Any(value => value.HasValue && value.Value < threshold);

    public bool Analyze<T>(T context, in AnalyzeArgs args, in AnalyzeResults results) where T : IMaaContext
    {
        try
        {
            ActionParamHelper.ThrowIfStopping(context);
            var param = ActionParamHelper.Parse(args.RecognitionParam);
            var threshold = FatigueRecognitionHelper.ResolveThreshold(
                (int?)param["threshold"], FatigueRecognitionHelper.GetThreshold());
            var values = FatigueRecognitionHelper.ReadFatigue(context, FatigueRecognitionHelper.FatigueRoisExpedition);

            if (!ShouldBrush(values, threshold))
            {
                LoggerHelper.Info($"[疲劳检测] 未识别到低于阈值{threshold}的成员，继续当前流程");
                return false;
            }

            var (_, lowestValue) = FatigueRecognitionHelper.FindLowest(values);
            FlowerStateTracker.CurrentFatigueLowest = lowestValue;
            LoggerHelper.Info($"[疲劳检测] 最低疲劳值={lowestValue}，低于阈值{threshold}，进入刷花");
            return true;
        }
        catch (MaaStopException)
        {
            return false;
        }
        catch (Exception exception)
        {
            LoggerHelper.Error($"[疲劳检测] 识别失败：{exception.Message}；按达标处理");
            return false;
        }
    }
}
