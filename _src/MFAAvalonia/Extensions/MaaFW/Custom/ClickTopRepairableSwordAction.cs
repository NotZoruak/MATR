using Avalonia;
using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Extensions;
using MFAAvalonia.Helper;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>
/// 点击修复界面最上方的可修复刀剑:
/// 先打开筛选界面并按用户配置选择刀种和伤势,确认后:
/// 在指定 ROI 内扫描白色标记像素(可修复刀剑的标记),取最上方白色区域的中心点击。
/// 当前视野内未找到时按滑动参数上滑列表继续查找,最多滑动 max_swipes 次。
/// 用于修复工坊选刀时避开出阵任务中使用的刀剑(出阵中的刀剑无白色标记)。
/// </summary>
public class ClickTopRepairableSwordAction : IMaaCustomAction
{
    public string Name { get; set; } = nameof(ClickTopRepairableSwordAction);

    /// <summary>扫描 ROI(可经 action_param 覆盖):修复界面刀剑列表左侧标记列</summary>
    public static readonly int[] DefaultRoi = [459, 127, 16, 554];

    /// <summary>列表 OCR ROI(可经 action_param 覆盖):刀剑名称列表区域</summary>
    public static readonly int[] DefaultListOcrRoi = [52, 127, 282, 554];

    /// <summary>白色标记像素下限/上限(可经 action_param 覆盖),默认 [252,252,252]~[255,255,255]</summary>
    public static readonly byte[] DefaultLower = [252, 252, 252];
    public static readonly byte[] DefaultUpper = [255, 255, 255];

    /// <summary>可接受白色标记的最小连续矩形尺寸</summary>
    public const int MinWhiteBlockWidth = 15;
    public const int MinWhiteBlockHeight = 7;

    /// <summary>最多滑动次数(可经 action_param 覆盖)</summary>
    public const int DefaultMaxSwipes = 8;

    /// <summary>长按滑动参数(可经 action_param 覆盖):按住起点 0.5s → 800ms 滑动到终点 → 再按住 1s</summary>
    public static readonly int[] DefaultSwipeFrom = [732, 638];
    public static readonly int[] DefaultSwipeTo = [732, 131];

    private static readonly (string Key, int X, int Y)[] SwordFilterPoints =
    [
        ("短", 272, 227), ("胁", 449, 230), ("打", 622, 229), ("太", 794, 230),
        ("大太", 283, 303), ("枪", 443, 303), ("薙", 625, 305), ("剑", 796, 302),
    ];

    private static readonly (string Key, int X, int Y)[] DamageFilterPoints =
    [
        ("轻伤", 274, 528), ("中伤", 445, 527), ("重伤", 622, 525),
    ];

    private static readonly int[] FilterButtonRoi = [944, 81, 105, 30];
    private static readonly int[] FilterPanelRoi = [494, 65, 79, 38];
    private static readonly int[] FilterEntryRoi = [765, 139, 66, 34];
    private static readonly int[] FilterConfirmRoi = [591, 602, 101, 44];
    private static readonly int[] NoRepairableSwordRoi = [676, 382, 53, 31];
    private const int FilterOpenAttempts = 10;
    private const int FilterRecognizeIntervalMs = 200;
    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        ActionParamHelper.ThrowIfStopping(context);
        var json = ActionParamHelper.Parse(args.ActionParam);
        try
        {
            if (!ApplyRepairFilter(context, json))
                return false;

            if (RecognizeText(context, NoRepairableSwordRoi, "没有"))
            {
                LoggerHelper.Info("[修刀选刀] OCR 命中“没有”，无符合条件刀剑，跳过扫描与上滑");
                RepairCooldownState.Start(DateTime.UtcNow);
                LoggerHelper.Info("[后勤修刀] 无符合条件刀剑，开始 30 分钟冷却");
                return false;
            }
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"[修刀选刀] 筛选流程异常：{e.Message}");
            return false;
        }
        var roi = json?["roi"]?.ToObject<int[]>() ?? DefaultRoi;
        var listOcrRoi = json?["list_ocr_roi"]?.ToObject<int[]>() ?? DefaultListOcrRoi;
        var lower = json?["lower"]?.ToObject<byte[]>() ?? DefaultLower;
        var upper = json?["upper"]?.ToObject<byte[]>() ?? DefaultUpper;
        var maxSwipes = json?["max_swipes"]?.ToObject<int>() ?? DefaultMaxSwipes;
        var swipeFrom = json?["swipe_from"]?.ToObject<int[]>() ?? DefaultSwipeFrom;
        var swipeTo = json?["swipe_to"]?.ToObject<int[]>() ?? DefaultSwipeTo;

        string? previousListOcr = null;
        for (int attempt = 0; attempt <= maxSwipes; attempt++)
        {
            ActionParamHelper.ThrowIfStopping(context);

            var currentListOcr = ReadRepairListOcr(context, listOcrRoi);
            if (RepairListOcrDecision.IsSameValidResult(previousListOcr, currentListOcr))
            {
                LoggerHelper.Info("[修刀选刀] 上滑后列表 OCR 未变化，判定已到列表底部");
                break;
            }
            if (RepairListOcrDecision.Normalize(currentListOcr) != null)
                previousListOcr = currentListOcr;

            var hit = ScanAndClick(context, roi, lower, upper);
            if (hit != null)
            {
                LoggerHelper.Info($"[修刀选刀] 找到可修复刀剑(第 {attempt} 屏),点击 ({hit.Value.X},{hit.Value.Y})");
                context.Click(hit.Value.X, hit.Value.Y);
                return true;
            }

            if (attempt >= maxSwipes)
                break;

            LoggerHelper.Info($"[修刀选刀] 当前视野未找到可修复刀剑，上滑列表({attempt + 1}/{maxSwipes})");
            // 与其它列表共用同一套 L 形上滑手势，避免惯性跳过行
            ListOcrScan.ScrollUp(context, [swipeFrom[0], swipeFrom[1], swipeTo[0], swipeTo[1]]);
            ActionParamHelper.SleepWithStopCheck(context, ListOcrScan.ScrollSettleMilliseconds);
        }

        LoggerHelper.Warning("[修刀选刀] 滑动后仍未找到可修复刀剑");
        RepairCooldownState.Start(DateTime.UtcNow);
        LoggerHelper.Info("[后勤修刀] 未找到可修复刀剑，开始 30 分钟冷却");
        return false;
    }

    /// <summary>打开筛选界面、点击已选条件并确认；没有已选条件时只打开后确认。</summary>
    private static bool ApplyRepairFilter<T>(T context, JObject? json) where T : IMaaContext
    {
        ActionParamHelper.ThrowIfStopping(context);

        var filterButtonX = FilterButtonRoi[0] + FilterButtonRoi[2] / 2;
        var filterButtonY = FilterButtonRoi[1] + FilterButtonRoi[3] / 2;
        var filterEntryX = FilterEntryRoi[0] + FilterEntryRoi[2] / 2;
        var filterEntryY = FilterEntryRoi[1] + FilterEntryRoi[3] / 2;

        var filterOpened = false;
        for (var attempt = 0; attempt < FilterOpenAttempts && !filterOpened; attempt++)
        {
            if (RecognizeText(context, FilterPanelRoi, "筛选"))
            {
                filterOpened = true;
                break;
            }

            if (RecognizeText(context, FilterButtonRoi, "筛选"))
            {
                context.Click(filterButtonX, filterButtonY);
                Thread.Sleep(FilterRecognizeIntervalMs);
            }
            else
            {
                LoggerHelper.Info(
                    $"[修刀选刀] 筛选面板尚未识别到，顶部不是筛选按钮，继续等待 ({attempt + 1}/{FilterOpenAttempts})");
                Thread.Sleep(FilterRecognizeIntervalMs);
            }
        }

        if (!filterOpened)
        {
            LoggerHelper.Warning("[修刀选刀] 未找到筛选面板，结束当前 action");
            return false;
        }

        context.Click(filterEntryX, filterEntryY);
        Thread.Sleep(200);

        var flags = new Dictionary<string, bool>();
        foreach (var point in SwordFilterPoints)
            flags[$"sword_type_{point.Key}"] = json?["sword_type_" + point.Key]?.Value<bool>() == true;
        foreach (var point in DamageFilterPoints)
            flags[$"damage_{point.Key}"] = json?["damage_" + point.Key]?.Value<bool>() == true;

        var selection = RepairFilterSelection.FromFlags(flags);
        foreach (var point in SwordFilterPoints)
        {
            if (!selection.SwordTypes.Contains(point.Key)) continue;
            ClickFilterOption(context, point.X, point.Y, point.Key);
        }
        foreach (var point in DamageFilterPoints)
        {
            if (!selection.DamageStates.Contains(point.Key)) continue;
            ClickFilterOption(context, point.X, point.Y, point.Key);
        }

        if (!RecognizeText(context, FilterConfirmRoi, "定"))
        {
            LoggerHelper.Warning("[修刀选刀] 未找到筛选确认按钮，结束当前 action");
            return false;
        }

        context.Click(
            FilterConfirmRoi[0] + FilterConfirmRoi[2] / 2,
            FilterConfirmRoi[1] + FilterConfirmRoi[3] / 2);
        FreezeRepairList(context);
        LoggerHelper.Info($"[修刀选刀] 筛选确认完成：刀种={string.Join(',', selection.SwordTypes)},伤势={string.Join(',', selection.DamageStates)}");
        return true;
    }

    /// <summary>等待筛选结果列表 1s，等待筛选后的列表稳定。</summary>
    private static void FreezeRepairList<T>(T context) where T : IMaaContext
    {
        ActionParamHelper.ThrowIfStopping(context);
        Thread.Sleep(1000);
    }

    /// <summary>识别指定区域内的文字。</summary>
    private static bool RecognizeText<T>(T context, int[] roi, string expected) where T : IMaaContext
    {
        using var image = context.GetImage();
        if (image == null) return false;
        var text = context.GetText(roi[0], roi[1], roi[2], roi[3], image);
        return expected == "筛选"
            ? RepairFilterSelection.IsFilterTitle(text)
            : text?.Contains(expected, StringComparison.Ordinal) == true;
    }

    /// <summary>读取修刀列表 OCR 文本；识别不到时返回空值。</summary>
    private static string? ReadRepairListOcr<T>(T context, int[] roi) where T : IMaaContext
    {
        using var image = context.GetImage();
        if (image == null) return null;
        return context.GetText(roi[0], roi[1], roi[2], roi[3], image);
    }

    /// <summary>点击筛选项并等待界面更新。</summary>
    private static void ClickFilterOption<T>(T context, int x, int y, string name) where T : IMaaContext
    {
        ActionParamHelper.ThrowIfStopping(context);
        context.Click(x, y);
        Thread.Sleep(500);
        LoggerHelper.Info($"[修刀选刀] 已选择筛选条件：{name}");
    }

    /// <summary>
    /// 扫描截图 ROI 内白色标记,返回最上方白色区域的点击坐标;未找到返回 null。
    /// </summary>
    private static (int X, int Y)? ScanAndClick<T>(T context, int[] roi, byte[] lower, byte[] upper) where T : IMaaContext
    {
        using var image = context.GetImage();
        if (image == null)
        {
            LoggerHelper.Warning("[修刀选刀] 获取截图失败");
            return null;
        }

        if (image is not MaaImageBuffer imageBuffer)
        {
            LoggerHelper.Warning("[修刀选刀] 截图缓冲区类型不受支持");
            return null;
        }

        using var bitmap = imageBuffer.ToBitmap();
        if (bitmap == null)
        {
            LoggerHelper.Warning("[修刀选刀] 截图转 Bitmap 失败");
            return null;
        }

        int x0 = roi[0], y0 = roi[1], w = roi[2], h = roi[3];
        if (w <= 0 || h <= 0 || x0 < 0 || y0 < 0 || x0 + w > bitmap.PixelSize.Width || y0 + h > bitmap.PixelSize.Height)
        {
            LoggerHelper.Warning($"[修刀选刀] ROI 越界: roi=[{x0},{y0},{w},{h}], 截图={bitmap.PixelSize.Width}x{bitmap.PixelSize.Height}");
            return null;
        }

        // 读取 ROI 区域像素(BGRA)
        var pixelBytes = new byte[w * h * 4];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixelBytes, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(x0, y0, w, h), handle.AddrOfPinnedObject(), pixelBytes.Length, w * 4);
        }
        finally
        {
            handle.Free();
        }

        var hit = FindSolidWhiteBlock(
            pixelBytes,
            w,
            h,
            lower,
            upper,
            MinWhiteBlockWidth,
            MinWhiteBlockHeight);
        if (hit == null)
            return null;

        var hitIndex = (hit.Value.Y * w + hit.Value.X) * 4;
        LoggerHelper.Info(
            $"[修刀选刀] 命中原始数据: ROI坐标=({hit.Value.X},{hit.Value.Y}), " +
            $"屏幕坐标=({x0 + hit.Value.X},{y0 + hit.Value.Y}), " +
            $"连续白色区域={MinWhiteBlockWidth}×{MinWhiteBlockHeight}, " +
            $"bytes=[{pixelBytes[hitIndex]},{pixelBytes[hitIndex + 1]},{pixelBytes[hitIndex + 2]},{pixelBytes[hitIndex + 3]}]");

        return (x0 + hit.Value.X, y0 + hit.Value.Y);
    }

    /// <summary>
    /// 从顶向下查找完整的连续白色矩形，并返回矩形中心；找不到时返回 null。
    /// </summary>
    public static (int X, int Y)? FindSolidWhiteBlock(
        byte[] pixelBytes,
        int width,
        int height,
        byte[] lower,
        byte[] upper,
        int blockWidth,
        int blockHeight)
    {
        if (width <= 0 || height <= 0 || blockWidth <= 0 || blockHeight <= 0
            || blockWidth > width || blockHeight > height
            || pixelBytes.Length < width * height * 4
            || lower.Length < 3 || upper.Length < 3)
            return null;

        for (var top = 0; top <= height - blockHeight; top++)
        {
            for (var left = 0; left <= width - blockWidth; left++)
            {
                var isSolidWhite = true;
                for (var y = top; y < top + blockHeight && isSolidWhite; y++)
                {
                    for (var x = left; x < left + blockWidth; x++)
                    {
                        var index = (y * width + x) * 4;
                        var b = pixelBytes[index];
                        var g = pixelBytes[index + 1];
                        var r = pixelBytes[index + 2];
                        if (r < lower[0] || r > upper[0]
                            || g < lower[1] || g > upper[1]
                            || b < lower[2] || b > upper[2])
                        {
                            isSolidWhite = false;
                            break;
                        }
                    }
                }

                if (isSolidWhite)
                    return (left + blockWidth / 2, top + blockHeight / 2);
            }
        }

        return null;
    }
}
