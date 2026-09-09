using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Helper;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>自动内番筛选面板：有优先目标时按其刀种筛选，无目标时保留全部可用刀剑。</summary>
public class NaibanFilterClickAction : IMaaCustomAction
{
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

    public string Name { get; set; } = nameof(NaibanFilterClickAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            var json = ActionParamHelper.Parse(args.ActionParam);
            int slot = json["slot"]?.Value<int>() ?? 0;
            if (!NaibanOutfitSelectionContext.TryGetTarget(slot, out var target))
            {
                LoggerHelper.Info($"[后勤] 第{slot}位没有优先目标，不限制刀种");
                return true;
            }

            if (!TypeCoords.TryGetValue(target.Type, out var coords))
            {
                LoggerHelper.Error($"[后勤] 刀种「{target.Type}」无筛选位置");
                return false;
            }

            context.Click(coords[0] + coords[2] / 2, coords[1] + coords[3] / 2);
            LoggerHelper.Info($"[后勤] 第{slot}位筛选「{target.Type}」：{target.Name}");
            return true;
        }
        catch (MaaStopException)
        {
            return false;
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"[后勤] 选择内番筛选条件失败: {e.Message}");
            return false;
        }
    }
}
