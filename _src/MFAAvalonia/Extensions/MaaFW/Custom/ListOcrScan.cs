using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;
using MFAAvalonia.Helper;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>
/// 列表 OCR 滚动扫描公共逻辑，供自定编队选刀、内番选刀、扫描诊断任务等共用：
/// OCR 找目标 → 点击 / 上滑翻页 / 与上屏相同判定到底。
/// </summary>
public static class ListOcrScan
{
    /// <summary>
    /// 大范围 OCR 得分阈值。
    /// 生僻字刀名会踩到这条线：2026-09-14 实机日志里「祢祢切丸」被识别成「称称切丸」时得分为 0.843，
    /// 低于 0.85 就会被判定成「列表里没有」，进而触发无谓的上滑与错位点击。
    /// </summary>
    public const double MinScore = 0.8;

    /// <summary>上滑后到下一次截图之间的等待（毫秒）。L 形收尾正常时列表应已停稳，这里只用于避开滑动前的旧帧</summary>
    public const int ScrollSettleMilliseconds = 150;

    /// <summary>上滑后等待列表停稳的最多取帧次数，超过则改用当前帧继续扫描（避免列表持续抖动时死等）</summary>
    public const int StableListMaxAttempts = 6;

    /// <summary>
    /// 刀剑列表 OCR 区域。右边界 262 只覆盖刀名列，避开列表右侧状态列（伤/轻伤/远征中…）的文字；
    /// 实测最长刀名「大千鸟十文字枪」右边界为 260。
    /// </summary>
    public static readonly int[] SwordListRoi = [98, 126, 164, 566];

    /// <summary>刀装/马匹列表 OCR 区域</summary>
    public static readonly int[] EquipListRoi = [943, 131, 176, 552];

    /// <summary>刀剑列表上滑：x, 起点 y, x, 终点 y。起点整体右移到状态列上方，避开刀名与刀种图标所在区域</summary>
    public static readonly int[] SwordScroll = [306, 624, 306, 128];

    /// <summary>刀装/马匹列表上滑：x, 起点 y, x, 终点 y。终点由 137 下移到 187，少滑 50px</summary>
    public static readonly int[] EquipScroll = [864, 534, 864, 187];

    /// <summary>对指定区域执行 OCR，返回全部识别结果（含 box/score/text），失败返回 null</summary>
    public static MaaExtensions.RecognitionQuery? OcrAll<T>(T context, IMaaImageBuffer image, int[] roi) where T : IMaaContext
    {
        var taskModel = new MaaNode
        {
            Name = "ListOcrScanOcr",
            Recognition = "OCR",
            Roi = new List<int>(roi),
        };
        var detail = context.RunRecognition(taskModel, image);
        if (detail?.Detail == null)
            return null;
        return JsonConvert.DeserializeObject<MaaExtensions.RecognitionQuery>(detail.Detail);
    }

    /// <summary>滚动扫描循环：OCR 找目标文本（得分 ≥ 阈值），命中执行点击动作；未命中上滑；与上屏相同判定到底返回 false。
    /// exactMatch=true 时用刀剑/刀装的字形归一精确匹配（一字之差不命中）；false 时用马匹的原有丢字容错匹配。
    /// 上滑后列表还在回弹，命中判定必须等到连续两帧 OCR 结果一致（列表静止）再做：
    /// 2026-09-14 实机即因为用了回弹过程中的 y 去点击，点到了相邻行的按钮，选刀页始终不变而卡住。</summary>
    public static bool ScanAndClick<T>(T context, string target, int[] roi, int[] scroll, Func<List<int>, bool> clickAction,
        string logTag, bool exactMatch = false) where T : IMaaContext
    {
        string lastOcr = string.Empty;
        string? lastFrameSignature = null;
        // 上滑之后必须先等列表静止，静止前的帧只用来判断「还在动」，不参与命中
        var waitingForStableList = false;
        var stableAttempts = 0;

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

            var frameSignature = BuildListSignature(all);
            if (waitingForStableList)
            {
                if (frameSignature != lastFrameSignature && stableAttempts < StableListMaxAttempts)
                {
                    lastFrameSignature = frameSignature;
                    stableAttempts++;
                    if (stableAttempts >= StableListMaxAttempts)
                        LoggerHelper.Warning($"[{logTag}] 列表持续抖动，改用当前帧继续扫描");
                    else
                    {
                        ActionParamHelper.SleepWithStopCheck(context, ScrollSettleMilliseconds);
                        continue;
                    }
                }
                waitingForStableList = false;
            }
            lastFrameSignature = frameSignature;

            // 命中多个时取最上方（y 最小）的匹配
            var hit = all
                .Where(r => r.Score >= MinScore && r.Text != null && (exactMatch
                    ? SwordNameMatcher.IsExactMatch(r.Text, target)
                    : SwordNameMatcher.IsLegacyFuzzyMatch(r.Text, target)))
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
            ActionParamHelper.SleepWithStopCheck(context, ScrollSettleMilliseconds);
            waitingForStableList = true;
            stableAttempts = 0;
            lastFrameSignature = null;
        }
    }

    /// <summary>列表帧签名：文本与坐标都一致才算同一画面。回弹位移只体现在 box 上，
    /// 得分会在同一画面上下浮动（实测同一刀名 0.843 ~ 0.900），因此不参与比较。</summary>
    private static string BuildListSignature(IEnumerable<MaaExtensions.RecognitionResult> results)
        => string.Join("|", results
            .Select(item => $"{(item.Box is { Count: >= 4 } ? string.Join(",", item.Box!) : "-")}:{item.Text}")
            .OrderBy(signature => signature, StringComparer.Ordinal));

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
            LoggerHelper.Info($"[ListOcrScan] 点击「确定」box=[{string.Join(",", hit.Box)}]");
            context.Click(cx, cy);
            ActionParamHelper.SleepWithStopCheck(context, 500);
            return true;
        }
        return false;
    }

    /// <summary>竖直拖动步数</summary>
    private const int VerticalScrollSteps = 14;

    /// <summary>竖直拖动每步间隔（毫秒），间隔越大越不容易被判定为快速甩动</summary>
    private const int VerticalStepDelayMilliseconds = 40;

    /// <summary>按下后的停顿（毫秒），确保按下被识别为拖拽起点而不是点击</summary>
    private const int TouchDownDelayMilliseconds = 20;

    /// <summary>L 形收尾的横向位移（像素）：抬手前的最后一段向左移动，用于消除列表惯性</summary>
    public const int InertiaBreakerOffset = 200;

    /// <summary>L 形收尾的横向步数</summary>
    private const int HorizontalScrollSteps = 10;

    /// <summary>
    /// L 形收尾每步间隔（毫秒）。收尾必须持续足够久（本配置约 400ms），
    /// 才能让游戏的滑动采样窗口里只有水平位移，从而在抬手时不产生竖直惯性。
    /// </summary>
    private const int HorizontalStepDelayMilliseconds = 40;

    /// <summary>
    /// 上滑手势：全程只用一次按下、一次抬手，不中途松手；竖直拖动到位后紧接着横向移动收尾（L 形），
    /// 使游戏在抬手前读到的最后几个采样都是水平方向，判定为无竖直速度，从而不产生列表惯性。
    /// 因此不需要在起点长按、也不需要等终点惯性停止。
    /// </summary>
    public static void ScrollUp<T>(T context, int[] scroll) where T : IMaaContext
    {
        var x = scroll[0];
        var startY = scroll[1];
        var endY = scroll[3];

        context.TouchDown(0, x, startY, 1);
        // 短暂停顿：确保按下被识别为拖拽起点而不是点击
        Thread.Sleep(TouchDownDelayMilliseconds);

        for (var i = 1; i <= VerticalScrollSteps; i++)
        {
            var y = startY - (startY - endY) * i / VerticalScrollSteps;
            context.TouchMove(0, x, y, 1);
            Thread.Sleep(VerticalStepDelayMilliseconds);
        }

        // L 形收尾：最后一整段只横向移动（途中不抬手），抬手时竖直速度为 0
        for (var i = 1; i <= HorizontalScrollSteps; i++)
        {
            var currentX = x - InertiaBreakerOffset * i / HorizontalScrollSteps;
            context.TouchMove(0, currentX, endY, 1);
            Thread.Sleep(HorizontalStepDelayMilliseconds);
        }

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
