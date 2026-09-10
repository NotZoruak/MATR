using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>
/// 活动目标 PT 检查动作：读取当前累计 PT，按计划日计算绝对目标。
/// 未达标返回 true 走 next 继续出阵；达标返回 false 走 on_error，
/// 由结束 node 写入结束原因并跳过当前队列项的剩余重复次数。
/// PT 识别失败或配置无效时一律不判定达标，保持既有流程。
/// </summary>
public class GoalPtCheckAction : IMaaCustomAction
{
    public string Name { get; set; } = nameof(GoalPtCheckAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            ActionParamHelper.ThrowIfStopping(context);

            var currentPt = ReadCurrentPt(context, args);
            var settings = ReadSettings();
            if (!settings.Enabled)
                return true;

            var absoluteGoal = settings.UseDatePlan
                ? ActivityGoalPtPlanner.GetAbsoluteGoal(
                    settings.TotalGoal, settings.StartDay!.Value, settings.EndDay!.Value,
                    ActivityGoalPtPlanner.GetPlanDay(DateTime.Now))
                : settings.TotalGoal;

            if (!ActivityGoalPtPlanner.IsGoalReached(currentPt, absoluteGoal))
                return true;

            LoggerHelper.Info($"[目标PT] 当前 {currentPt}，目标 {absoluteGoal}，跳过剩余轮数");

            // 返回 false 走 on_error，由结束 node 写入结束原因并跳过剩余轮数
            return false;
        }
        catch (MaaStopException)
        {
            return false;
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"[目标PT] 错误: {e.Message}");
            return true;
        }
    }

    /// <summary>
    /// 读取 pipeline 传入的累计 PT。识别失败时返回 null，不判定达标。
    /// </summary>
    private static long? ReadCurrentPt<T>(T context, in RunArgs args) where T : IMaaContext
    {
        var json = ActionParamHelper.Parse(args.ActionParam);
        var roi = json["roi"] is JArray { Count: 4 } roiArray
            ? roiArray.ToObject<int[]>()
            : null;
        if (roi == null)
            return null;

        using var image = context.GetImage();
        if (image == null)
            return null;

        var text = context.GetText(roi[0], roi[1], roi[2], roi[3], image);
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // 累计 PT 可能带千位分隔符，去除后只保留数字
        var digits = new string(text.Where(char.IsDigit).ToArray());
        return long.TryParse(digits, out var value) ? value : null;
    }

    /// <summary>
    /// 从当前任务定义解析目标 PT 配置。
    /// </summary>
    private static ActivityGoalPtSettings ReadSettings()
    {
        var task = MaaProcessor.Processors
            .Select(processor => processor.GetActiveTaskDefinition())
            .FirstOrDefault(definition => definition != null);
        if (task == null)
            return ActivityGoalPtSettings.Parse(null, null, null, null, null);

        var goalOption = task.Option?.FirstOrDefault(option => option.Name == "HP_目标PT");
        var subOptions = goalOption?.SubOptions;
        var totalGoalOption = subOptions?.FirstOrDefault(option => option.Name == "HP_目标总PT");
        var datePlanOption = subOptions?.FirstOrDefault(option => option.Name == "HP_日期计划");
        var dateSubOptions = datePlanOption?.SubOptions;

        return ActivityGoalPtSettings.Parse(
            IsSwitchEnabled(goalOption) ? ["Yes"] : null,
            ReadData(totalGoalOption, "total_goal"),
            IsSwitchEnabled(datePlanOption) ? ["Yes"] : null,
            ReadData(dateSubOptions?.FirstOrDefault(option => option.Name == "HP_计划开始日期"), "start_date"),
            ReadData(dateSubOptions?.FirstOrDefault(option => option.Name == "HP_计划截至日期"), "end_date"));
    }

    private static string? ReadData(MaaInterface.MaaInterfaceSelectOption? option, string key) =>
        option?.Data?.TryGetValue(key, out var value) == true ? value : null;

    /// <summary>
    /// 判断 switch 选项是否处于开启状态。switch 以 cases 的索引保存选择，索引 0 为关闭。
    /// </summary>
    private static bool IsSwitchEnabled(MaaInterface.MaaInterfaceSelectOption? option) =>
        option?.Index > 0 || option?.SelectedCases?.Contains("Yes") == true;
}
