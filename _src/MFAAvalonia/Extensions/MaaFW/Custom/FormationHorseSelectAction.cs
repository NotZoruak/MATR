using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Helper;
using Newtonsoft.Json.Linq;
using System;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>马匹列表选择：OCR 找当前位目标马匹，命中双击中心，未命中滑动</summary>
public class FormationHorseSelectAction : IMaaCustomAction
{
    public string Name { get; set; } = nameof(FormationHorseSelectAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            int pos = FormationContext.CurrentSlot;
            if (pos < 1 || pos > 6)
            {
                LoggerHelper.Error($"[FormationHorseSelect] 当前装备槽位未初始化: {pos}");
                return false;
            }

            return SelectHorse(context, pos);
        }
        catch (MaaStopException)
        {
            LoggerHelper.Info("[FormationHorseSelect] 手动停止");
            return false;
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"[FormationHorseSelect] Error: {e.Message}");
            return false;
        }
    }

    /// <summary>在马匹列表中选择指定槽位的目标马匹（OCR 找 → 双击中心），供状态机与节点共用</summary>
    public static bool SelectHorse<T>(T context, int pos) where T : IMaaContext
    {
        string target = FormationContext.Horses[pos - 1];
        if (string.IsNullOrEmpty(target) || target == "无")
        {
            LoggerHelper.Info($"[FormationHorseSelect] 槽位{pos} 马匹为「无」，跳过");
            return true;
        }

        LoggerHelper.Info($"[FormationHorseSelect] 槽位{pos} 寻找马匹: {target}");

        return ListOcrScan.ScanAndClick(
            context,
            target,
            ListOcrScan.EquipListRoi,
            ListOcrScan.EquipScroll,
            box =>
            {
                // 第一次点击：命中马匹位置中心；未出现「确定」时重试再点，最多 8 次
                int cx = box[0] + box[2] / 2;
                int cy = box[1] + box[3] / 2;
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    context.Click(cx, cy);
                    ActionParamHelper.SleepWithStopCheck(context, 500);

                    // 第二次点击：OCR 找「确定」并点击，冻结 100ms
                    if (ListOcrScan.ClickConfirm(context))
                        return true;
                }
                LoggerHelper.Error("[FormationHorseSelect] 重试后仍未找到「确定」按钮");
                return false;
            },
            "FormationHorseSelect");
    }
}
