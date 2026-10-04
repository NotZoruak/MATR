using Avalonia;
using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using System.Diagnostics;
using System;
using System.Linq;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>刷花时按筛选条件选择最上方已上锁的刀剑。</summary>
public sealed class FlowerBrushSwordSelectionAction : IMaaCustomAction
{
    private static readonly (string Key, int X, int Y)[] SwordTypeButtons =
    [
        ("type_short", 276, 229), ("type_wakizashi", 450, 229), ("type_uchigatana", 625, 229), ("type_tachi", 799, 229),
        ("type_odachi", 276, 303), ("type_yari", 450, 303), ("type_naginata", 625, 303), ("type_ken", 799, 303),
    ];
    private static readonly (string Key, int X, int Y)[] ConditionButtons =
    [
        ("first", 276, 455), ("kiwame", 450, 455), ("favorite", 625, 455), ("unequipped", 799, 455),
        ("troop_equipped", 276, 526), ("horse_equipped", 450, 526), ("charm_equipped", 625, 526), ("treasure_equipped", 799, 526),
    ];
    private const int MaxSwipeLimit = 50;
    private const int ClickWaitMilliseconds = 500;
    private const int ConfirmFreezeMilliseconds = 200;
    private const int ScreenTransitionAttempts = 30;
    private static readonly int[] FilterConfirmButtonRoi = [540, 592, 200, 66];
    private static readonly int[] FilterTitleRoi = [480, 48, 140, 55];

    public string Name { get; set; } = nameof(FlowerBrushSwordSelectionAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            ActionParamHelper.ThrowIfStopping(context);
            var parameters = ActionParamHelper.Parse(args.ActionParam);
            var maxSwipes = Math.Clamp((int?)parameters["max_swipes"] ?? 5, 0, MaxSwipeLimit);

            if (!ApplyFilters<T>(context, parameters))
                return true;

            for (var swipe = 0; swipe <= maxSwipes; swipe++)
            {
                ActionParamHelper.ThrowIfStopping(context);
                var lockedSwordY = FindTopLockedSword(context);
                if (lockedSwordY.HasValue)
                {
                    LoggerHelper.Info($"[刷花选刀] 第 {swipe} 次滑动后命中上锁刀剑，点击 (1208,{lockedSwordY.Value})");
                    ClickAndWait(context, 1208, lockedSwordY.Value);
                    if (!WaitForSwordSelectionExit(context))
                    {
                        LoggerHelper.Warning("[刷花选刀] 点击上锁刀剑后仍停留在刀剑选择页面，停止重复筛选");
                        return true;
                    }
                    return true;
                }

                if (swipe == maxSwipes)
                    break;

                LoggerHelper.Info($"[刷花选刀] 当前画面没有上锁刀剑，执行 L 形滑动 {swipe + 1}/{maxSwipes}");
                ScrollList(context);
                ActionParamHelper.SleepWithStopCheck(context, 150);
            }

            LoggerHelper.Warning($"[刷花选刀] 已检查初始画面及 {maxSwipes} 次滑动，仍未找到上锁刀剑");
            return false;
        }
        catch (MaaStopException)
        {
            LoggerHelper.Info("[刷花选刀] 手动停止");
            return false;
        }
        catch (Exception exception)
        {
            LoggerHelper.Error($"[刷花选刀] 选择失败：{exception.Message}");
            return true;
        }
    }

    private static bool ApplyFilters<T>(T context, Newtonsoft.Json.Linq.JObject parameters) where T : IMaaContext
    {
        ClickAndWait(context, 862, 94);
        if (!WaitForFilterPanel(context))
        {
            LoggerHelper.Warning("[刷花选刀] 未识别到筛选面板");
            return false;
        }

        ClickAndWait(context, 801, 153);

        var anySwordTypeSelected = SwordTypeButtons.Any(button => (bool?)parameters[button.Key] == true);
        if (anySwordTypeSelected)
            ClickSelectedButtons(context, parameters, SwordTypeButtons);
        ClickSelectedButtons(context, parameters, ConditionButtons);

        ClickAndWait(context, 1010, 454);
        ActionParamHelper.ThrowIfStopping(context);
        context.Click(640, 625);
        ActionParamHelper.SleepWithStopCheck(context, ClickWaitMilliseconds);
        WaitForRegionFreeze(context, FilterConfirmButtonRoi, ConfirmFreezeMilliseconds);

        if (!WaitForSwordList(context))
        {
            LoggerHelper.Warning("[刷花选刀] 确认筛选后未回到刀剑列表");
            return false;
        }

        return EnsureSakuraAscending(context);
    }

    private static void ClickSelectedButtons<T>(T context, Newtonsoft.Json.Linq.JObject parameters,
        (string Key, int X, int Y)[] buttons) where T : IMaaContext
    {
        foreach (var button in buttons)
        {
            ActionParamHelper.ThrowIfStopping(context);
            if ((bool?)parameters[button.Key] != true)
                continue;
            ClickAndWait(context, button.X, button.Y);
        }
    }

    private static bool WaitForFilterPanel<T>(T context) where T : IMaaContext
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            ActionParamHelper.ThrowIfStopping(context);
            if (ContainsOcrText(context, [727, 127, 132, 51], "取消筛选"))
                return true;
            ActionParamHelper.SleepWithStopCheck(context, 150);
        }
        return false;
    }

    private static bool WaitForSwordList<T>(T context) where T : IMaaContext
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            ActionParamHelper.ThrowIfStopping(context);
            var isSwordList = ContainsOcrText(context, [530, 6, 224, 45], "刀剑男士选择");
            var isFilterPanelOpen = ContainsOcrText(context, FilterTitleRoi, "筛选")
                || ContainsOcrText(context, FilterConfirmButtonRoi, "确定");
            if (isSwordList && !isFilterPanelOpen)
                return true;
            ActionParamHelper.SleepWithStopCheck(context, 150);
        }
        return false;
    }

    private static bool WaitForSwordSelectionExit<T>(T context) where T : IMaaContext
    {
        for (var attempt = 0; attempt < ScreenTransitionAttempts; attempt++)
        {
            ActionParamHelper.ThrowIfStopping(context);
            if (!ContainsOcrText(context, [530, 6, 224, 45], "刀剑男士选择"))
                return true;
            ActionParamHelper.SleepWithStopCheck(context, 150);
        }

        return false;
    }

    private static bool EnsureSakuraAscending<T>(T context) where T : IMaaContext
    {
        var switchedFromDescending = false;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            ActionParamHelper.ThrowIfStopping(context);
            var descending = FindOcrText(context, [939, 77, 56, 39], "降序");
            if (!switchedFromDescending && descending != null)
            {
                ClickAndWait(context, descending.Box![0] + descending.Box[2] / 2, descending.Box[1] + descending.Box[3] / 2);
                switchedFromDescending = true;
                continue;
            }

            if (FindOcrText(context, [939, 77, 56, 39], "升序") != null)
                return true;

            ActionParamHelper.SleepWithStopCheck(context, 150);
        }

        LoggerHelper.Warning("[刷花选刀] 未能确认樱吹雪升序状态");
        return false;
    }

    private static bool ContainsOcrText<T>(T context, int[] roi, string expected) where T : IMaaContext
        => FindOcrText(context, roi, expected) != null;

    private static void ClickAndWait<T>(T context, int x, int y) where T : IMaaContext
    {
        ActionParamHelper.ThrowIfStopping(context);
        context.Click(x, y);
        ActionParamHelper.SleepWithStopCheck(context, ClickWaitMilliseconds);
    }

    private static void WaitForRegionFreeze<T>(T context, int[] roi, int freezeMilliseconds) where T : IMaaContext
    {
        byte[]? previous = null;
        var stableSince = Stopwatch.StartNew();
        while (stableSince.ElapsedMilliseconds < 5000)
        {
            ActionParamHelper.ThrowIfStopping(context);
            var current = ReadRegionPixels(context, roi);
            if (current != null && previous != null && current.AsSpan().SequenceEqual(previous))
            {
                if (stableSince.ElapsedMilliseconds >= freezeMilliseconds)
                    return;
            }
            else
            {
                stableSince.Restart();
            }

            previous = current;
            ActionParamHelper.SleepWithStopCheck(context, 50);
        }
    }

    private static byte[]? ReadRegionPixels<T>(T context, int[] roi) where T : IMaaContext
    {
        using var image = context.GetImage();
        if (image is not MaaImageBuffer imageBuffer)
            return null;

        using var bitmap = imageBuffer.ToBitmap();
        if (bitmap == null || bitmap.PixelSize.Width < roi[0] + roi[2] || bitmap.PixelSize.Height < roi[1] + roi[3])
            return null;

        var bytes = new byte[roi[2] * roi[3] * 4];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(roi[0], roi[1], roi[2], roi[3]), handle.AddrOfPinnedObject(), bytes.Length, roi[2] * 4);
        }
        finally
        {
            handle.Free();
        }

        return bytes;
    }

    private static MaaExtensions.RecognitionResult? FindOcrText<T>(T context, int[] roi, string expected) where T : IMaaContext
    {
        using var image = context.GetImage();
        var query = image == null ? null : ListOcrScan.OcrAll(context, image, roi);
        return query?.All.FirstOrDefault(result => result.Text?.Contains(expected, StringComparison.Ordinal) == true
            && result.Box is { Count: >= 4 });
    }

    private static int? FindTopLockedSword<T>(T context) where T : IMaaContext
    {
        using var image = context.GetImage();
        if (image is not MaaImageBuffer imageBuffer)
            return null;

        using var bitmap = imageBuffer.ToBitmap();
        if (bitmap == null || bitmap.PixelSize.Width < 1209 || bitmap.PixelSize.Height < 689)
            return null;

        const int x = 59;
        const int top = 126;
        const int height = 563;
        var bytes = new byte[height * 4];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(x, top, 1, height), handle.AddrOfPinnedObject(), bytes.Length, 4);
        }
        finally
        {
            handle.Free();
        }

        for (var offset = 0; offset < height; offset++)
        {
            var index = offset * 4;
            if (bytes[index + 2] == 227 && bytes[index + 1] == 202 && bytes[index] == 109)
                return top + offset;
        }

        return null;
    }

    private static void ScrollList<T>(T context) where T : IMaaContext
    {
        context.TouchDown(0, 596, 624, 100);
        try
        {
            ActionParamHelper.SleepWithStopCheck(context, 20);
            MoveSegment(context, 596, 624, 596, 128, 560, 14);
            MoveSegment(context, 596, 128, 873, 128, 400, 10);
        }
        finally
        {
            context.TouchUp(0);
        }
    }

    private static void MoveSegment<T>(T context, int startX, int startY, int endX, int endY, int durationMilliseconds, int steps)
        where T : IMaaContext
    {
        for (var step = 1; step <= steps; step++)
        {
            ActionParamHelper.ThrowIfStopping(context);
            var x = startX + (endX - startX) * step / steps;
            var y = startY + (endY - startY) * step / steps;
            context.TouchMove(0, x, y, 100);
            ActionParamHelper.SleepWithStopCheck(context, durationMilliseconds / steps);
        }
    }
}
