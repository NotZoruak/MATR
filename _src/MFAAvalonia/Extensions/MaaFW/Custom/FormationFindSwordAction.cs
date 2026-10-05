using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Helper;
using Newtonsoft.Json.Linq;
using System;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>刀剑选择列表滚动扫描找刀，并校验刀剑锁定与可选状态。</summary>
public class FormationFindSwordAction : IMaaCustomAction
{
    private readonly Action<string> _addLog;

    public string Name { get; set; } = nameof(FormationFindSwordAction);

    public FormationFindSwordAction(Action<string> addLog) => _addLog = addLog;

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
            var nodeName = args.NodeName;

            return ListOcrScan.ScanAndClick(
                context,
                target,
                ListOcrScan.SwordListRoi,
                ListOcrScan.SwordScroll,
                (image, box) =>
                {
                    if (context.ColorMatch(190, 190, 190, 190, 190, 190, image, out _,
                            threshold: 1.0, x: 1191, y: box[1], w: 1, h: 1, count: 1))
                    {
                        if (!context.OverrideNext(nodeName, []))
                        {
                            _addLog($"[自定编队] 「{target}」无法选中，结束当前编队预设失败");
                            return false;
                        }

                        _addLog($"[自定编队] 「{target}」右侧按钮为灰色，刀剑无法选中，跳过当前预设");
                        return true;
                    }

                    // 点击行右侧按钮：x 固定 1191，y 取命中文字左上角 y，单点
                    context.Click(1191, box[1]);
                    return true;
                },
                // 默认精确匹配：一字之差不得命中，避免「太郎太刀/次郎太刀」近似名误选
                "FormationFindSword",
                hitFilter: (image, box) => context.ColorMatch(212, 173, 31, 212, 173, 31, image, out _,
                    threshold: 1.0, x: 18, y: box[1], w: 1, h: 1, count: 1));
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
