using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Helper;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>刀剑选择页筛选面板：按当前槽位刀名对应刀种，点击刀种按钮</summary>
public class FormationFilterClickAction : IMaaCustomAction
{
    public string Name { get; set; } = nameof(FormationFilterClickAction);

    /// <summary>刀种点击位置（1280×720 基准）</summary>
    private static readonly Dictionary<string, int[]> TypeCoords = new()
    {
        ["短刀"] = [239, 210, 61, 35],
        ["胁差"] = [423, 212, 62, 35],
        ["打刀"] = [594, 212, 62, 34],
        ["太刀"] = [763, 211, 61, 35],
        ["大太刀"] = [248, 287, 61, 35],
        ["枪"] = [415, 287, 61, 35],
        ["薙刀"] = [592, 285, 61, 35],
        ["剑"] = [760, 286, 61, 35],
    };

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            var json = ActionParamHelper.Parse(args.ActionParam);
            int slot = json["slot"]?.Value<int>() ?? 0;
            if (slot < 1 || slot > 6)
            {
                LoggerHelper.Error($"[FormationFilter] 槽位非法: {slot}");
                return false;
            }

            string swordName = FormationContext.Swords[slot - 1];
            var type = FormationContext.GetSwordType(swordName);
            if (type == null)
            {
                LoggerHelper.Error($"[FormationFilter] 刀名「{swordName}」未在刀种映射表中");
                return false;
            }

            if (!TypeCoords.TryGetValue(type, out var coords))
            {
                LoggerHelper.Error($"[FormationFilter] 刀种「{type}」无点击位置");
                return false;
            }

            LoggerHelper.Info($"[FormationFilter] 槽位{slot}「{swordName}」→ 刀种「{type}」→ 点击 [{string.Join(",", coords)}]");
            int cx = coords[0] + coords[2] / 2;
            int cy = coords[1] + coords[3] / 2;
            context.ClickWithProgress(cx, cy);
            return true;
        }
        catch (MaaStopException)
        {
            LoggerHelper.Info("[FormationFilter] 手动停止");
            return false;
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"[FormationFilter] Error: {e.Message}");
            return false;
        }
    }
}
