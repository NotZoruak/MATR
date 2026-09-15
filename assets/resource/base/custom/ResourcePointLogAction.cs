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

        // 奖励文本直接取本 node 识别命中的结果：识别读不到 expected 时不会执行 action。
        // 命中气泡的那一帧弹窗文字常常只渲染出资源名（如「获得木炭」），
        // 数量（「×50」）要再过一两百毫秒才出现，因此识别结果只作初值，后面继续读完整文本。
        var text = ReadRecognitionText(args);
        if (!ContainsExpected(text, expected))
        {
            // 兜底：识别详情缺失（旧管道）时才重新读屏一次
            text = ReadText(context, roi);
        }

        var bestText = ContainsExpected(text, expected) ? text : string.Empty;
        var bestParts = ResourcePointRewardParser.Parse(bestText).Count;

        // 弹窗存活期内持续读：优先记住能解析出资源数量的读数，数量相同时以后读到的为准
        var closed = false;
        var startTime = System.DateTime.UtcNow;
        while ((System.DateTime.UtcNow - startTime).TotalMilliseconds < timeout)
        {
            ActionParamHelper.ThrowIfStopping(context);
            ActionParamHelper.SleepWithStopCheck(context, pollInterval);

            var current = ReadText(context, roi);
            if (!ContainsExpected(current, expected))
            {
                closed = true;
                break;
            }

            var currentParts = ResourcePointRewardParser.Parse(current).Count;
            if (currentParts > bestParts || (currentParts == bestParts && currentParts > 0))
            {
                bestText = current;
                bestParts = currentParts;
            }
        }

        if (bestParts == 0)
        {
            // 只读到资源名、没读到数量时不能编造数值。这里用 Info 而不是 Warning：
            // Warning 档词条会被工作记录收进「特殊情况」，而这是打点侧的内部兜底。
            LoggerHelper.Info($"[资源点] 未读到奖励数量，跳过本次打点: {bestText}");
        }
        else
        {
            LoggerHelper.Info($"[资源点] OCR 稳定识别结果: {bestText}");
            LogGained(prefix, bestText);
        }

        // 弹窗未消失前回枢纽会被再次命中并重复打点，因此等它收起再返回
        if (closed)
            LoggerHelper.Info($"[资源点] OCR 已无法识别“{expected}”，结束等待");
        else
            LoggerHelper.Warning($"[资源点] 等待 OCR 消失超时（{timeout}ms），结束等待");
        return true;
    }

    // 奖励文字是否已经读到；expected 为识别判据，与识别 node 的 expected 必须一致
    private static bool ContainsExpected(string text, string expected) =>
        text.Replace(" ", string.Empty).Contains(expected, System.StringComparison.Ordinal);

    // 取本 node 识别结果里的文本。OCR 的 best 只保留命中 expected 的那一条，
    // 因此识别命中时 best.text 就是弹窗文字（弹窗刚出现时可能只有资源名）。
    private static string ReadRecognitionText(in RunArgs args)
    {
        var detail = args.RecognitionDetail;
        if (detail?.Detail is not { Length: > 0 } raw)
            return string.Empty;

        var query = JsonConvert.DeserializeObject<MaaExtensions.RecognitionQuery>(raw);
        return query?.Best?.Text ?? string.Empty;
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
