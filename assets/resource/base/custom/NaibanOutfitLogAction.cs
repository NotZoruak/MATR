using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Extensions;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>
/// 识别内番完成对话中的内番服立绘，并将结果写入后勤词表日志。
/// </summary>
public class NaibanOutfitLogAction : IMaaCustomAction
{
    private readonly NaibanOutfitRecognitionState _state = new();

    public string Name { get; set; } = nameof(NaibanOutfitLogAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        var json = ActionParamHelper.Parse(args.ActionParam);
        var mode = (string?)json["mode"] ?? "recognize";

        switch (mode)
        {
            case "begin":
                _state.Begin();
                return true;
            case "finish":
                if (_state.TryFinishMissingOutfit())
                {
                    LoggerHelper.Info("[后勤] 未显示内番服立绘");
                    // 失败时补充名条原文：判断是漏字、形近误识还是名条位置偏移都靠这几行
                    foreach (var reading in _state.FailedReadings)
                        LoggerHelper.Info($"[后勤] 内番服识别失败 {reading}");
                }
                else if (_state.SwordNames.Count > 0)
                {
                    LoggerHelper.Info($"[后勤] 内番服 {string.Join("、", _state.SwordNames)}");
                    if (json["sync_swordbook"]?.Value<bool>() == true)
                    {
                        var catalogPath = Path.Combine(AppPaths.ResourceDirectory, "base", "SwordBookCatalog.json");
                        var synchronizedNames = SwordBookNaibanOutfitService.MarkOwnedOutfits(_state.SwordNames, catalogPath);
                        if (synchronizedNames.Count > 0)
                            LoggerHelper.Info($"[后勤] 已同步刀帐内番服 {string.Join("、", synchronizedNames)}");
                        var unsynchronizedNames = _state.SwordNames.Except(synchronizedNames, StringComparer.Ordinal).ToList();
                        if (unsynchronizedNames.Count > 0)
                            LoggerHelper.Warning($"[后勤] 刀帐中未找到已拥有的内番服刀剑 {string.Join("、", unsynchronizedNames)}");
                    }
                }
                return true;
            case "recognize":
                RecognizeAndClick(context, json);
                return true;
            default:
                LoggerHelper.Warning($"[后勤] 未知内番服识别模式: {mode}");
                return true;
        }
    }

    private void RecognizeAndClick<T>(T context, JObject json) where T : IMaaContext
    {
        try
        {
            var indicatorRoi = ParseRoi(json["indicator_roi"] as JArray, "内番服提示 OCR ROI");
            var nameRoi = ParseRoi(json["name_roi"] as JArray, "内番服刀剑 OCR ROI");
            var indicatorText = Normalize(ReadText(context, indicatorRoi));
            var nameText = Normalize(ReadText(context, nameRoi));

            // 名条为「刀种 + 刀名」，OCR 会漏识生僻字（薙、杵）或把形近字读错（蛉→岭），
            // 因此与刀剑掉落、自定编队共用同一套归一与唯一匹配。
            if ((indicatorText.Contains('饲') || indicatorText.Contains('耕'))
                && TryResolveSwordName(nameText, out var swordName))
                _state.TryRecord(swordName);
            else
                _state.NoteFailedReading(indicatorText, nameText);
        }
        finally
        {
            // 动画可能持续多帧；每次命中颜色都必须点击，不能因重复日志去重而跳过关闭操作。
            var click = ParseRoi(json["click"] as JArray, "内番对话关闭区域");
            ClickRectangle(context, click);
        }
    }

    /// <summary>
    /// 解析内番对话名条中的刀剑名：与刀剑掉落、自定编队共用同一套归一与唯一匹配，
    /// 只在刀帐中唯一命中时才接受，避免把内番服记到其它刀剑名下。
    /// </summary>
    private static bool TryResolveSwordName(string text, out string swordName)
    {
        // 刀帐映射按需加载并复用 FormationContext 缓存，避免对话循环每帧重复解析目录文件
        var map = FormationContext.SwordTypeMap;
        if (map == null || map.Count == 0)
        {
            map = FormationContext.LoadSwordTypeMap();
            FormationContext.SwordTypeMap = map;
        }

        return SwordNameResolver.TryResolve(text, map, out swordName);
    }

    private static string ReadText<T>(T context, int[] roi) where T : IMaaContext
    {
        using var image = context.GetImage();
        if (image == null)
            return string.Empty;

        var node = new MaaNode
        {
            Name = "NaibanOutfitOCR",
            Recognition = "OCR",
            Roi = roi,
        };
        var detail = context.RunRecognition(node, image);
        if (detail?.Detail == null)
            return string.Empty;

        var query = JsonConvert.DeserializeObject<MaaExtensions.RecognitionQuery>(detail.Detail);
        return query?.Best?.Text ?? string.Empty;
    }

    private static void ClickRectangle<T>(T context, int[] rectangle) where T : IMaaContext
    {
        var x = rectangle[0] + (rectangle[2] > 0 ? Random.Shared.Next(rectangle[2]) : 0);
        var y = rectangle[1] + (rectangle[3] > 0 ? Random.Shared.Next(rectangle[3]) : 0);
        context.Click(x, y);
    }

    private static int[] ParseRoi(JArray? value, string name)
    {
        if (value == null || value.Count != 4)
            throw new Exception($"{name}必须是 [x, y, w, h]");
        return value.ToObject<int[]>()!;
    }

    private static string Normalize(string text) => Regex.Replace(text ?? string.Empty, @"\s+", string.Empty);
}
