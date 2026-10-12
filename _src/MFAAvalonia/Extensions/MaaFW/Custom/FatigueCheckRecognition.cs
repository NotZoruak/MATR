using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Helper;
using System;
using System.Linq;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>按当前流程条件识别队伍疲劳状态。</summary>
public sealed class FatigueCheckRecognition : IMaaCustomRecognition
{
    public string Name { get; set; } = nameof(FatigueCheckRecognition);

    /// <summary>至少一个已识别疲劳值低于阈值时返回 true。</summary>
    public static bool ShouldBrush(int?[] fatigueValues, int threshold)
        => fatigueValues.Any(value => value.HasValue && value.Value < threshold);

    public bool Analyze<T>(T context, in AnalyzeArgs args, in AnalyzeResults results) where T : IMaaContext
    {
        string? mode = null;
        try
        {
            ActionParamHelper.ThrowIfStopping(context);
            var param = ActionParamHelper.Parse(args.RecognitionParam);
            mode = (string?)param["mode"];

            if (string.Equals(mode, "check_sortie_first", StringComparison.Ordinal))
            {
                var sortieThreshold = FatigueRecognitionHelper.ResolveThreshold((int?)param["threshold"], 30);
                using var image = context.GetImage();
                if (image == null)
                {
                    LoggerHelper.Info("[疲劳检测-出阵] 未获取到截图，识别未命中");
                    return false;
                }

                var firstValue = FatigueRecognitionHelper.ParseFatigueValue(
                    FatigueRecognitionHelper.ReadFatigueText(
                        context,
                        FatigueRecognitionHelper.FatigueRoisSortie[0],
                        image));
                if (!firstValue.HasValue)
                {
                    LoggerHelper.Info("[疲劳检测-出阵] 首位疲劳值未识别，识别未命中");
                    return false;
                }

                if (firstValue.Value >= sortieThreshold)
                {
                    LoggerHelper.Info($"[疲劳检测-出阵] 首位={firstValue}，达到阈值{sortieThreshold}，识别未命中");
                    return false;
                }

                FlowerStateTracker.CurrentFatigueLowest = firstValue.Value;
                LoggerHelper.Info($"[疲劳检测-出阵] 首位={firstValue}，低于阈值{sortieThreshold}，识别命中");
                return true;
            }

            var threshold = FatigueRecognitionHelper.ResolveThreshold(
                (int?)param["threshold"], FatigueRecognitionHelper.GetThreshold(context));
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
            if (string.Equals(mode, "check_sortie_first", StringComparison.Ordinal))
            {
                LoggerHelper.Error($"[疲劳检测-出阵] 识别异常：{exception.Message}，交由 on_error 处理");
                throw;
            }

            LoggerHelper.Error($"[疲劳检测] 识别失败：{exception.Message}；按达标处理");
            return false;
        }
    }
}
