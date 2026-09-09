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
    private static readonly string[] SwordTypes =
    [
        "大太刀", "短刀", "胁差", "打刀", "太刀", "薙刀", "枪", "剑"
    ];

    private static readonly string[] SimilarCharacterGroups =
    [
        "掘堀",
        "広广厂",
        "國国",
    ];

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
                    LoggerHelper.Info("[后勤] 未显示内番服立绘");
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

            if ((indicatorText.Contains('饲') || indicatorText.Contains('耕'))
                && TryValidateSword(ReadText(context, nameRoi), out var swordName))
                _state.TryRecord(swordName);
        }
        finally
        {
            // 动画可能持续多帧；每次命中颜色都必须点击，不能因重复日志去重而跳过关闭操作。
            var click = ParseRoi(json["click"] as JArray, "内番对话关闭区域");
            ClickRectangle(context, click);
        }
    }

    private static bool TryValidateSword(string text, out string swordName)
    {
        swordName = string.Empty;
        var normalized = Normalize(text);
        var swordType = SwordTypes.FirstOrDefault(normalized.StartsWith);
        if (swordType == null)
            return false;

        var recognizedName = normalized[swordType.Length..];
        if (recognizedName.Length < 2)
            return false;

        var map = FormationContext.LoadSwordTypeMap();
        if (map.TryGetValue(recognizedName, out var exactType)
            && string.Equals(exactType, swordType, StringComparison.Ordinal))
        {
            swordName = recognizedName;
            return true;
        }

        var candidates = map
            .Where(pair => string.Equals(pair.Value, swordType, StringComparison.Ordinal))
            .Select(pair => pair.Key)
            .Where(candidate => IsSimilarName(recognizedName, candidate))
            .ToList();
        if (candidates.Count != 1)
            return false;

        swordName = candidates[0];
        return true;
    }

    private static bool IsSimilarName(string recognizedName, string candidate)
    {
        if (recognizedName.Length != candidate.Length)
            return false;

        var differences = 0;
        for (var i = 0; i < recognizedName.Length; i++)
        {
            if (recognizedName[i] == candidate[i])
                continue;

            if (!SimilarCharacterGroups.Any(group => group.Contains(recognizedName[i]) && group.Contains(candidate[i])))
                return false;

            differences++;
            if (differences > 1)
                return false;
        }

        return differences == 1;
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
