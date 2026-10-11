using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Helper;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>在部队选择页查找缺少刀装的刀剑，并点击对应行的一键装备按钮。</summary>
public sealed class EquipmentFallbackAction : IMaaCustomAction
{
    private static readonly int[] EquipmentListRoi = [427, 129, 174, 551];

    public string Name { get; set; } = nameof(EquipmentFallbackAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            using var image = context.GetImage();
            if (image == null)
            {
                LoggerHelper.Warning("[EquipmentFallback] 未获取到部队选择页截图");
                return false;
            }

            var task = new MaaNode
            {
                Name = "EquipmentFallbackMissingCheck",
                Recognition = "TemplateMatch",
                Template = ["Common/刀装不足.png"],
                Roi = new List<int>(EquipmentListRoi),
                Threshold = new List<double> { 0.9 },
            };
            var detail = context.RunRecognition(task, image);
            var query = detail?.Detail == null
                ? null
                : JsonConvert.DeserializeObject<MaaExtensions.RecognitionQuery>(detail.Detail);
            var box = query?.Best?.Box;
            if (box is not { Count: >= 4 })
            {
                LoggerHelper.Info("[EquipmentFallback] 未发现缺少刀装的刀剑");
                return false;
            }

            var target = EquipmentFallbackDecision.GetOneClickEquipButtonTarget(box[1]);
            LoggerHelper.Info($"[EquipmentFallback] 缺装标记=[{string.Join(",", box)}]，点击一键装备 [{target.X},{target.Y}]");
            context.ClickWithProgress(target.X, target.Y);
            return true;
        }
        catch (MaaStopException)
        {
            LoggerHelper.Info("[EquipmentFallback] 手动停止");
            return false;
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"[EquipmentFallback] Error: {e.Message}");
            return false;
        }
    }
}
