using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Helper;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>
/// 仅在有效累计 PT 达到当前目标时识别命中，由同一个 node 提前结束当前队列项。
/// 未达标、无效配置、无法读取 PT 或识别异常时均不命中，继续活动原流程。
/// </summary>
public sealed class GoalPtCheckRecognition : IMaaCustomRecognition
{
    public string Name { get; set; } = nameof(GoalPtCheckRecognition);

    public bool Analyze<T>(T context, in AnalyzeArgs args, in AnalyzeResults results) where T : IMaaContext
    {
        try
        {
            ActionParamHelper.ThrowIfStopping(context);
            var settings = ReadSettings(context);
            if (!settings.Enabled)
                return false;

            var param = ActionParamHelper.Parse(args.RecognitionParam);
            var roi = param["roi"] is JArray { Count: 4 } array ? array.ToObject<int[]>() : null;
            if (roi == null || roi[0] < 0 || roi[1] < 0 || roi[2] <= 0 || roi[3] <= 0)
                return false;

            using var image = context.GetImage();
            if (image == null)
                return false;

            var currentPt = ParseCurrentPt(context.GetText(roi[0], roi[1], roi[2], roi[3], image));
            if (!ShouldSkip(currentPt, settings, DateTime.Now))
                return false;

            LoggerHelper.Info($"[目标PT] 当前 {currentPt} 已达到目标，跳过剩余轮数");
            return true;
        }
        catch (MaaStopException)
        {
            return false;
        }
        catch (Exception exception)
        {
            LoggerHelper.Warning($"[目标PT] 识别异常，继续任务：{exception.Message}");
            return false;
        }
    }

    /// <summary>只接受单个非负整数及其千位逗号形式，避免把多个数字拼接为虚假的累计值。</summary>
    public static long? ParseCurrentPt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var value = text.Trim();
        if (!Regex.IsMatch(value, @"\A(?:[0-9]+|[0-9]{1,3}(?:[,，][0-9]{3})+)\z"))
            return null;

        value = value.Replace(",", string.Empty).Replace("，", string.Empty);
        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result)
            ? result : null;
    }

    /// <summary>按开关和日期计划判断是否达标，未知 PT 或尚未开始的计划始终不命中。</summary>
    public static bool ShouldSkip(long? currentPt, ActivityGoalPtSettings settings, DateTime localTime)
    {
        if (!settings.Enabled || settings.TotalGoal <= 0 || currentPt is null or < 0)
            return false;
        if (settings.UseDatePlan && (!settings.StartDay.HasValue || !settings.EndDay.HasValue))
            return false;

        var absoluteGoal = settings.UseDatePlan
            ? ActivityGoalPtPlanner.GetAbsoluteGoal(settings.TotalGoal,
                settings.StartDay!.Value, settings.EndDay!.Value,
                ActivityGoalPtPlanner.GetPlanDay(localTime))
            : settings.TotalGoal;
        return ActivityGoalPtPlanner.IsGoalReached(currentPt, absoluteGoal);
    }

    /// <summary>
    /// 从当前任务定义解析目标 PT 配置。选项名按后缀匹配，兼容各任务自己的前缀。
    /// </summary>
    private static ActivityGoalPtSettings ReadSettings<T>(T context) where T : IMaaContext
    {
        // 多实例并行时必须按当前 MaaTasker 定位，不能读取任意实例的活动任务。
        var task = MaaProcessor.ResolveByTasker(context.Tasker)?.GetActiveTaskDefinition();
        if (task == null)
            return ActivityGoalPtSettings.Parse(null, null, null, null, null);

        var goalOption = FindBySuffix(task.Option, "目标PT");
        var subOptions = goalOption?.SubOptions;
        var totalGoalOption = FindBySuffix(subOptions, "目标总PT");
        var datePlanOption = FindBySuffix(subOptions, "日期计划");
        var dateSubOptions = datePlanOption?.SubOptions;

        return ActivityGoalPtSettings.Parse(
            IsSwitchEnabled(goalOption) ? ["Yes"] : null,
            ReadData(totalGoalOption, "total_goal"),
            IsSwitchEnabled(datePlanOption) ? ["Yes"] : null,
            ReadData(FindBySuffix(dateSubOptions, "计划开始日期"), "start_date"),
            ReadData(FindBySuffix(dateSubOptions, "计划截至日期"), "end_date"));
    }

    /// <summary>
    /// 按名称后缀查找选项，兼容各任务自己的选项前缀。
    /// </summary>
    private static MaaInterface.MaaInterfaceSelectOption? FindBySuffix(
        IEnumerable<MaaInterface.MaaInterfaceSelectOption>? options, string suffix)
        => options?.FirstOrDefault(option =>
            (option.Name ?? string.Empty).EndsWith(suffix, StringComparison.Ordinal));

    private static string? ReadData(MaaInterface.MaaInterfaceSelectOption? option, string key) =>
        option?.Data?.TryGetValue(key, out var value) == true ? value : null;

    /// <summary>
    /// 判断 switch 选项是否处于开启状态。switch 以 cases 的索引保存选择，索引 0 为关闭。
    /// </summary>
    private static bool IsSwitchEnabled(MaaInterface.MaaInterfaceSelectOption? option) =>
        option?.Index > 0 || option?.SelectedCases?.Contains("Yes") == true;
}
