using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Helper;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>刀装列表选择：OCR 找当前位目标刀装（用户输入切分词直接匹配），命中双击中心，未命中滑动</summary>
public class FormationEquipSelectAction : IMaaCustomAction
{
    public string Name { get; set; } = nameof(FormationEquipSelectAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            var json = ActionParamHelper.Parse(args.ActionParam);
            int slot = json["slot"]?.Value<int>() ?? 0;
            if (slot < 1 || slot > 3)
            {
                LoggerHelper.Error($"[FormationEquipSelect] 刀装槽位非法: {slot}");
                return false;
            }

            int pos = FormationContext.CurrentSlot;
            if (pos < 1 || pos > 6)
            {
                LoggerHelper.Error($"[FormationEquipSelect] 当前装备槽位未初始化: {pos}");
                return false;
            }

            return SelectEquip(context, pos, slot);
        }
        catch (MaaStopException)
        {
            LoggerHelper.Info("[FormationEquipSelect] 手动停止");
            return false;
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"[FormationEquipSelect] Error: {e.Message}");
            return false;
        }
    }

    /// <summary>在刀装列表中选择指定槽位的目标刀装：OCR 找 → 点击命中位置 → OCR 找「确定」点击，供状态机与节点共用</summary>
    public static bool SelectEquip<T>(T context, int pos, int slot) where T : IMaaContext
    {
        var equipList = FormationContext.SplitEquip(FormationContext.Equips[pos - 1]);
        if (slot > equipList.Count)
        {
            LoggerHelper.Info($"[FormationEquipSelect] 第{pos}位刀剑男士没有第{slot}个刀装槽，跳过");
            return true;
        }

        string target = equipList[slot - 1];
        LoggerHelper.Info($"[FormationEquipSelect] 第{pos}位刀剑男士的刀装槽{slot}寻找刀装：{target}");

        return ListOcrScan.ScanAndClick(
            context,
            target,
            ListOcrScan.EquipListRoi,
            ListOcrScan.EquipScroll,
            (_, box) => ListOcrScan.ClickTargetAndConfirm(context, box, "FormationEquipSelect"),
            // 默认精确匹配：只允许字形容错，不允许一字之差命中异种刀装
            "FormationEquipSelect",
            (ocrText, expected) =>
                !ocrText.Contains("新", StringComparison.Ordinal)
                && SwordNameMatcher.IsExactMatch(ocrText, expected));
    }

}
