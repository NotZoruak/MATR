using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Helper;
using Newtonsoft.Json.Linq;
using System;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>刀剑选择列表滚动扫描找刀：OCR 命中目标刀名后点击行右侧按钮 [1191, y, 1, 1]</summary>
public class FormationFindSwordAction : IMaaCustomAction
{
    public string Name { get; set; } = nameof(FormationFindSwordAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            var json = ActionParamHelper.Parse(args.ActionParam);
            int slot = json["slot"]?.Value<int>() ?? 0;
            if (slot < 1 || slot > 6)
            {
                LoggerHelper.Error($"[FormationFindSword] 槽位非法: {slot}");
                return false;
            }

            string target = FormationContext.Swords[slot - 1];
            if (string.IsNullOrEmpty(target))
            {
                LoggerHelper.Error($"[FormationFindSword] 槽位 {slot} 未配置刀剑");
                return false;
            }

            LoggerHelper.Info($"[FormationFindSword] 槽位{slot} 寻找刀剑: {target}");

            return FormationScan.ScanAndClick(
                context,
                target,
                FormationScan.SwordListRoi,
                FormationScan.SwordScroll,
                box =>
                {
                    // 点击行右侧按钮：x 固定 1191，y 取命中文字左上角 y，单点
                    context.Click(1191, box[1]);
                    return true;
                },
                "FormationFindSword",
                // 刀剑必须精确匹配：一字之差不得命中，避免「太郎太刀/次郎太刀」近似名误选
                exactMatch: true);
        }
        catch (MaaStopException)
        {
            LoggerHelper.Info("[FormationFindSword] 手动停止");
            return false;
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"[FormationFindSword] Error: {e.Message}");
            return false;
        }
    }
}
