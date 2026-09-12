using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using System;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>
/// 同步后勤专用：判断某支部队在「后勤」任务中是否设置为休息。
///
/// 管线的 E_CheckTeamN 由任务启动时的配置合并结果决定，配置在任务运行中被修改、
/// 或合并与选图动作取到不同来源时，休息的部队仍会进入选图流程；
/// 此时 ExpeditionMapSelectAction 因休息返回失败，流程会反复回到本丸查看远征。
/// 因此在检查部队之前先用该识别判定：命中即跳过该部队，直接检查下一支部队。
/// </summary>
public sealed class ExpeditionTeamRestRecognition : IMaaCustomRecognition
{
    public string Name { get; set; } = nameof(ExpeditionTeamRestRecognition);

    public bool Analyze<T>(T context, in AnalyzeArgs args, in AnalyzeResults results) where T : IMaaContext
    {
        try
        {
            var teamLabel = (string?)ActionParamHelper.Parse(args.RecognitionParam)["team_label"];
            if (string.IsNullOrWhiteSpace(teamLabel))
                return false;

            var processor = MaaProcessor.ResolveByTasker(context.Tasker);
            if (processor == null)
            {
                // 无法判断所属实例时交给原有检查链，避免误跳过。
                LoggerHelper.Warning($"[同步后勤] 无法定位执行任务的实例，{teamLabel} 按正常检查流程处理");
                return false;
            }

            var mapIndex = processor.GetLogisticsTeamMapIndex(teamLabel);
            if (mapIndex is > 0)
                return false;

            LoggerHelper.Info(mapIndex == null
                ? $"[同步后勤] {teamLabel} 在后勤配置中没有目的地（未选择），跳过本次派遣检查"
                : $"[同步后勤] {teamLabel} 在后勤配置中为休息，跳过本次派遣检查");
            return true;
        }
        catch (Exception e)
        {
            LoggerHelper.Warning($"[同步后勤] 休息判定异常，按正常检查流程处理：{e.Message}");
            return false;
        }
    }
}
