using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;
using MFAAvalonia.Extensions.MaaFW;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>共享疲劳值 OCR 请求、识别结果读取与文本解析逻辑。</summary>
public static class FatigueRecognitionHelper
{
    /// <summary>远征队伍页面六个疲劳 OCR 区域。</summary>
    public static readonly int[][] FatigueRoisExpedition =
    [
        [839, 190, 77, 22],
        [839, 284, 77, 22],
        [839, 379, 77, 22],
        [839, 473, 77, 22],
        [839, 568, 77, 22],
        [839, 662, 77, 22],
    ];

    /// <summary>出阵编队页面六个疲劳 OCR 区域。</summary>
    public static readonly int[][] FatigueRoisSortie =
    [
        [340, 187, 80, 22],
        [340, 282, 80, 22],
        [340, 376, 80, 22],
        [340, 471, 80, 22],
        [340, 565, 80, 22],
        [340, 660, 80, 22],
    ];

    /// <summary>创建疲劳 OCR 请求，固定区域识别统一使用 only_rec。</summary>
    public static MaaNode CreateFatigueOcrNode(int[] roi) => new()
    {
        Name = "FatigueCheckOcr",
        Recognition = "OCR",
        OnlyRec = true,
        Roi = new List<int>(roi),
    };

    /// <summary>OCR 六个位置的疲劳值，空槽位或失败返回 null。</summary>
    public static int?[] ReadFatigue<T>(T context, int[][] rois) where T : IMaaContext
    {
        var values = new int?[6];
        using var image = context.GetImage();
        if (image == null)
            return values;

        for (int i = 0; i < 6; i++)
        {
            var roi = rois[i];
            values[i] = ParseFatigueValue(ReadFatigueText(context, roi, image));
        }

        return values;
    }

    /// <summary>读取单个疲劳区域中的 OCR 文本。</summary>
    public static string? ReadFatigueText<T>(T context, int[] roi, IMaaImageBuffer image) where T : IMaaContext
    {
        var detail = context.RunRecognition(CreateFatigueOcrNode(roi), image);
        if (string.IsNullOrWhiteSpace(detail?.Detail))
            return null;

        return JsonConvert.DeserializeObject<MaaExtensions.RecognitionQuery>(detail.Detail)?.Best?.Text;
    }

    /// <summary>解析疲劳 OCR 文本中的数值，并修正常见字母误识别。</summary>
    public static int? ParseFatigueValue(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var clean = text.Trim().Replace('B', '8').Replace('O', '0').Replace('S', '5');
        if (clean.Contains('/')) clean = clean.Split('/')[0];
        return int.TryParse(clean.Trim(), out var value) && value >= 0 ? value : null;
    }

    /// <summary>找最低疲劳值的索引和值；无可用位置返回 (-1, -1)。</summary>
    public static (int Index, int Value) FindLowest(int?[] values)
    {
        int bestPos = -1, bestVal = int.MaxValue;
        for (int i = 0; i < 6; i++)
        {
            if (!values[i].HasValue) continue;
            if (values[i].Value < bestVal) { bestVal = values[i].Value; bestPos = i; }
        }

        return (bestPos, bestVal);
    }

    /// <summary>获取长期远征疲劳阈值，默认 91。</summary>
    public static int GetThreshold()
    {
        var planOpt = ExpeditionOptionResolver.FindEnabledLongTermPlanOption(
            ExpeditionOptionResolver.GetCurrentTasks());
        var fatigueOpt = planOpt?.SubOptions?.FirstOrDefault(option => option.Name == "疲劳阈值");
        int? configuredThreshold = null;
        if (fatigueOpt?.Data != null
            && fatigueOpt.Data.TryGetValue("threshold", out var value)
            && int.TryParse(value, out var threshold))
            configuredThreshold = threshold;

        return ResolveThreshold(configuredThreshold, 91);
    }

    /// <summary>应用阈值有效性规则；未设置或小于等于零时使用默认值。</summary>
    public static int ResolveThreshold(int? configuredThreshold, int defaultThreshold)
        => configuredThreshold is > 0 ? configuredThreshold.Value : defaultThreshold;
}
