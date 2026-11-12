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
            LoggerHelper.Info($"[FormationHorseSelect] 第{pos}位刀剑男士的马匹为「无」，跳过");
            return true;
        }

        LoggerHelper.Info($"[FormationHorseSelect] 第{pos}位刀剑男士寻找马匹：{target}");

        return ListOcrScan.ScanAndClick(
            context,
            target,
            ListOcrScan.EquipListRoi,
            ListOcrScan.EquipScroll,
            (_, box) => ListOcrScan.ClickTargetAndConfirm(context, box, "FormationHorseSelect"),
            // 马匹同样精确匹配，仅「高楯黑」允许 OCR 漏识「楯」；否则祝一号/祝十号会互相误选
            "FormationHorseSelect",
            SwordNameMatcher.IsHorseMatch);
    }
}
