using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Extensions;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

public class ResourcePointLogAction : IMaaCustomAction
{
    public string Name { get; set; } = nameof(ResourcePointLogAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        var json = ActionParamHelper.Parse(args.ActionParam);
        var roi = ParseRoi(json["roi"] as JArray ?? throw new System.Exception("资源点 OCR ROI 缺失"));
        var expected = (string?)json["expected"] ?? "获得";
        var timeout = Math.Max(1000, (int?)json["timeout"] ?? 10000);
        var pollInterval = Math.Max(50, (int?)json["poll_interval"] ?? 200);
        // task 参数决定打点前缀（合战场/地下城）；缺省回退旧前缀 [资源点]
        var task = (string?)json["task"] ?? string.Empty;
        var prefix = string.IsNullOrWhiteSpace(task) ? "[资源点]" : $"[{task}]";
        var startTime = System.DateTime.UtcNow;

        var text = ReadText(context, roi);
        var lastVisibleText = text;

        while ((System.DateTime.UtcNow - startTime).TotalMilliseconds < timeout)
        {
            ActionParamHelper.ThrowIfStopping(context);
            ActionParamHelper.SleepWithStopCheck(context, pollInterval);

            text = ReadText(context, roi);
            if (!text.Replace(" ", string.Empty).Contains(expected, System.StringComparison.Ordinal))
            {
                LoggerHelper.Info($"[资源点] OCR 稳定识别结果: {lastVisibleText}");
                LogGained(prefix, lastVisibleText);
                LoggerHelper.Info($"[资源点] OCR 已无法识别“{expected}”，结束等待");
                return true;
            }

            lastVisibleText = text;
        }

        LoggerHelper.Warning($"[资源点] 等待 OCR 消失超时（{timeout}ms），当前结果: {text}");
        LoggerHelper.Info($"[资源点] OCR 稳定识别结果: {lastVisibleText}");
        LogGained(prefix, lastVisibleText);
        return true;
    }

    // 把 OCR 文本（如「获得木炭×20」）转成打点格式「木炭x20」，多个资源以空格分隔
    private static void LogGained(string prefix, string text)
    {
        var parts = ResourcePointRewardParser.Parse(text);
        if (parts.Count == 0)
            return;

        LoggerHelper.Info($"{prefix} 资源点获取 {string.Join(" ", parts)}");
    }

    private static string ReadText<T>(T context, int[] roi) where T : IMaaContext
    {
        using var image = context.GetImage();
        if (image == null)
            return string.Empty;

        var taskModel = new MaaNode
        {
            Name = "ResourcePointOCR",
            Recognition = "OCR",
            Roi = roi
        };
        var detail = context.RunRecognition(taskModel, image);
        if (detail == null || string.IsNullOrWhiteSpace(detail.Detail))
            return string.Empty;

        var query = JsonConvert.DeserializeObject<MaaExtensions.RecognitionQuery>(detail.Detail);
        return query?.Best?.Text ?? string.Empty;
    }

    private static int[] ParseRoi(JArray roi)
    {
        if (roi.Count != 4)
            throw new System.Exception("资源点 OCR ROI 必须是 [x, y, w, h]");
        return roi.ToObject<int[]>()!;
    }
}

// 注意：CustomClassLoader 对 custom 目录中的每个 .cs 文件单独编译成独立程序集，
// 跨文件引用会导致编译失败、action 无法注册，因此本辅助类必须与 action 同文件定义。
// 资源点奖励解析：把 OCR 文本（如「获得木炭×20」）转成打点格式「木炭x20」，多个资源以空格分隔
internal static class ResourcePointRewardParser
{
    public static IReadOnlyList<string> Parse(string text)
    {
        var parts = new List<string>();
        // OCR 可能把乘号识别为全角乘号或半角字母 x/X，甚至混出「x×65」这类变体（如「获得玉钢x×65」），
        // 因此乘号段允许连续出现多个 [×xX]。
        var matches = Regex.Matches(text, @"获得\s*(?<name>[^×xX\s]+)[×xX]+(?<count>\d+)");
        foreach (Match match in matches)
            parts.Add($"{match.Groups["name"].Value}x{match.Groups["count"].Value}");

        // 资源点中的委托符固定只会掉落一个；数量末位被 OCR 截断时按该规则补全。
        if (parts.Count == 0 && Regex.IsMatch(text, @"获得\s*委托符\s*[×xX]+\s*$"))
            parts.Add("委托符x1");

        return parts;
    }
}
