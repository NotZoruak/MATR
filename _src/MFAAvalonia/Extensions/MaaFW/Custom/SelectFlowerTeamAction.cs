using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Helper;
using System;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>
/// 长期远征计划——根据记录的部队编号选择刷花目标部队。
/// 读取 FlowerStateTracker.TargetTeam，点击对应部队按钮。
/// </summary>
public class SelectFlowerTeamAction : IMaaCustomAction
{
    public string Name { get; set; } = nameof(SelectFlowerTeamAction);

    /// <summary>部队 1~5 的点击坐标（同 E_SelectTeamBtnN）</summary>
    private static readonly int[][] TeamTargets =
    [
        [154, 93],   // 部队一
        [276, 94],   // 部队二
        [405, 91],   // 部队三
        [522, 89],   // 部队四
        [638, 91],   // 部队五
    ];

    /// <summary>部队 1~5 的装备解除选项点击区域</summary>
    private static readonly int[][] EquipRemoveTeamTargets =
    [
        [348, 387, 59, 29],
        [526, 388, 59, 29],
        [701, 390, 59, 29],
        [875, 387, 59, 29],
        [351, 458, 60, 30],
    ];

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            ActionParamHelper.ThrowIfStopping(context);
            var team = FlowerStateTracker.TargetTeam;
            if (team < 1 || team > 5)
            {
                LoggerHelper.Warning($"[选队刷花] 无效部队编号: {team}");
                return false;
            }

            var parameters = ActionParamHelper.Parse(args.ActionParam);
            if ((string?)parameters["mode"] == "remove_equip")
            {
                var clickPoint = GetEquipRemoveClickPoint(team)!.Value;
                LoggerHelper.Info($"[刷花装备解除] 点击部队{team}的解除选项");
                context.Tasker.Click(clickPoint.X, clickPoint.Y);
                return true;
            }

            var t = TeamTargets[team - 1];
            LoggerHelper.Info($"[选队刷花] 选择部队{team}，点击 ({t[0]}, {t[1]})");
            context.Tasker.Click(t[0], t[1]);
            return true;
        }
        catch (MaaStopException) { return false; }
        catch (Exception e) { LoggerHelper.Error($"[选队刷花] 错误: {e.Message}"); return false; }
    }

    internal static (int X, int Y)? GetEquipRemoveClickPoint(int team)
    {
        if (team < 1 || team > EquipRemoveTeamTargets.Length)
            return null;

        var target = EquipRemoveTeamTargets[team - 1];
        return (target[0] + target[2] / 2, target[1] + target[3] / 2);
    }
}
