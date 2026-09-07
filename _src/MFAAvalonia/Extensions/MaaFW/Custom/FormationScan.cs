using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;
using MFAAvalonia.Helper;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>自定编队滚动扫描公共逻辑：OCR 找目标 → 点击 / 滑动循环 / 与上屏相同判定到底</summary>
public static class FormationScan
{
    /// <summary>大范围 OCR 得分阈值</summary>
    public const double MinScore = 0.85;

    /// <summary>刀剑列表 OCR 区域</summary>
    public static readonly int[] SwordListRoi = [98, 126, 235, 566];

    /// <summary>刀装/马匹列表 OCR 区域</summary>
    public static readonly int[] EquipListRoi = [943, 131, 176, 552];

    /// <summary>刀剑列表上滑：x, 起点 y, x, 终点 y</summary>
    public static readonly int[] SwordScroll = [106, 624, 106, 128];

    /// <summary>刀装/马匹列表上滑</summary>
    public static readonly int[] EquipScroll = [864, 534, 864, 137];

    /// <summary>对指定区域执行 OCR，返回全部识别结果（含 box/score/text），失败返回 null</summary>
    public static MaaExtensions.RecognitionQuery? OcrAll<T>(T context, IMaaImageBuffer image, int[] roi) where T : IMaaContext
    {
        var taskModel = new MaaNode
        {
            Name = "FormationScanOcr",
            Recognition = "OCR",
            Roi = new List<int>(roi),
        };
        var detail = context.RunRecognition(taskModel, image);
        if (detail?.Detail == null)
            return null;
        return JsonConvert.DeserializeObject<MaaExtensions.RecognitionQuery>(detail.Detail);
    }

    /// <summary>滚动扫描循环：OCR 找目标文本（得分 ≥ 阈值），命中执行点击动作；未命中上滑；与上屏相同判定到底返回 false。
    /// exactMatch=true 时用刀剑/刀装的字形归一精确匹配（一字之差不命中）；false 时用马匹的原有丢字容错匹配。</summary>
    public static bool ScanAndClick<T>(T context, string target, int[] roi, int[] scroll, Func<List<int>, bool> clickAction,
        string logTag, bool exactMatch = false) where T : IMaaContext
    {
        string lastOcr = string.Empty;
        while (true)
        {
            ActionParamHelper.ThrowIfStopping(context);

            using var image = context.GetImage();
            if (image == null)
            {
                ActionParamHelper.SleepWithStopCheck(context, 300);
                continue;
            }

            var query = OcrAll(context, image, roi);
            var all = query?.All ?? [];
            // 命中多个时取最上方（y 最小）的匹配
            var hit = all
                .Where(r => r.Score >= MinScore && r.Text != null && (exactMatch
                    ? FormationNameMatcher.IsExactMatch(r.Text, target)
                    : FormationNameMatcher.IsLegacyFuzzyMatch(r.Text, target)))
                .Where(r => r.Box is { Count: >= 4 })
                .OrderBy(r => r.Box![1])
                .FirstOrDefault();

            if (hit?.Box is { Count: >= 4 })
            {
                LoggerHelper.Info($"[{logTag}] 命中「{hit.Text}」box=[{string.Join(",", hit.Box)}]");
                if (clickAction(hit.Box))
                    return true;
                return false;
            }

            // 到底判定：OCR 结果与上一屏完全相同
            var current = string.Join("|", all.Select(r => r.Text).OrderBy(t => t, StringComparer.Ordinal));
            if (current == lastOcr)
            {
                LoggerHelper.Error($"[{logTag}] 列表已到底，未找到「{target}」");
                return false;
            }
            lastOcr = current;

            ScrollUp(context, scroll);
            ActionParamHelper.SleepWithStopCheck(context, 300);
        }
    }

    /// <summary>刀装/马匹确定按钮 OCR 区域（右侧按钮列）</summary>
    public static readonly int[] ConfirmRoi = [1027, 130, 54, 561];

    /// <summary>OCR 右侧按钮列找「确定」并点击（命中多个取最上方），冻结 100ms</summary>
    public static bool ClickConfirm<T>(T context) where T : IMaaContext
    {
        using var image = context.GetImage();
        if (image == null)
            return false;

        var query = OcrAll(context, image, ConfirmRoi);
        var hit = query?.All
            .Where(r => r.Score >= MinScore && r.Text != null && r.Text.Contains("确定", StringComparison.Ordinal))
            .Where(r => r.Box is { Count: >= 4 })
            .OrderBy(r => r.Box![1])
            .FirstOrDefault();

        if (hit?.Box is { Count: >= 4 })
        {
            int cx = hit.Box[0] + hit.Box[2] / 2;
            int cy = hit.Box[1] + hit.Box[3] / 2;
            LoggerHelper.Info($"[FormationScan] 点击「确定」box=[{string.Join(",", hit.Box)}]");
            context.Click(cx, cy);
            ActionParamHelper.SleepWithStopCheck(context, 500);
            return true;
        }
        return false;
    }

    /// <summary>上滑手势：按住起点 500ms → 滑动 800ms → 终点保持 1s → 松开</summary>
    public static void ScrollUp<T>(T context, int[] scroll) where T : IMaaContext
    {
        context.TouchDown(0, scroll[0], scroll[1], 1);
        Thread.Sleep(500);

        int steps = 20;
        for (int i = 1; i <= steps; i++)
        {
            int y = scroll[1] - (scroll[1] - scroll[3]) * i / steps;
            context.TouchMove(0, scroll[0], y, 1);
            Thread.Sleep(800 / steps);
        }

        Thread.Sleep(1000);
        context.TouchUp(0);
    }

    /// <summary>双击指定坐标中心，两次点击同位置，首次点击后冻结 200ms（第二次不识别）</summary>
    public static void DoubleClickCenter<T>(T context, List<int> box) where T : IMaaContext
    {
        int cx = box[0] + box[2] / 2;
        int cy = box[1] + box[3] / 2;
        context.Click(cx, cy);
        ActionParamHelper.SleepWithStopCheck(context, 500);
        context.Click(cx, cy);
        ActionParamHelper.SleepWithStopCheck(context, 500);
    }
}
