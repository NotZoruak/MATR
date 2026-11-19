using Avalonia;
using Avalonia.Media.Imaging;
using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>扫描新习合列表前六个位置，选择锁刀或乱舞等级低于7级的刀剑。</summary>
public sealed class NewMixTargetSelectionAction : IMaaCustomAction
{
    private static readonly int[][] MarkerRois =
    [
        [88, 127, 7, 8],
        [88, 228, 7, 8],
        [88, 329, 7, 8],
        [88, 430, 7, 8],
        [88, 531, 7, 8],
        [88, 632, 7, 8]
    ];

    private static readonly int[][] LevelRois =
    [
        [614, 148, 16, 17],
        [614, 249, 16, 17],
        [614, 351, 17, 18],
        [614, 452, 18, 19],
        [614, 552, 18, 21],
        [614, 653, 19, 22]
    ];

    private static readonly int[][] SelectTargets =
    [
        [1216, 164],
        [1216, 265],
        [1216, 366],
        [1216, 467],
        [1216, 568],
        [1216, 669]
    ];

    private const byte EmptyLower = 218;
    private const byte EmptyUpper = 222;
    private const byte UnlockedLower = 41;
    private const byte UnlockedUpper = 45;
    private const byte LockedR = 212;
    private const byte LockedG = 173;
    private const byte LockedB = 31;
    private const byte ColorTolerance = 2;
    private const int ScanTimeoutMs = 20000;
    private const int ScanIntervalMs = 200;
    private const int LevelOcrAttempts = 5;
    private const int ExpandedLevelOcrMargin = 4;

    public string Name { get; set; } = nameof(NewMixTargetSelectionAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            ActionParamHelper.ThrowIfStopping(context);
            var slots = ReadSlotsUntilConfirmed(context);
            var plan = NewMixTargetSelectionDecision.Decide(slots);
            NewMixTargetSelectionState.Current = plan;

            LoggerHelper.Info($"[新习合] 目标扫描结果：{plan.Outcome}，位置：{plan.Position}");
            if (plan.Outcome is NewMixTargetSelectionOutcome.NoSword or NewMixTargetSelectionOutcome.Completed)
                return true;

            if (plan.Outcome == NewMixTargetSelectionOutcome.Unreadable)
            {
                LoggerHelper.Warning($"[新习合] 第 {plan.Position} 位乱舞等级无法识别，停止本轮选择");
                return false;
            }

            var target = SelectTargets[plan.Position - 1];
            context.ClickWithProgress(target[0], target[1]);
            LoggerHelper.Info($"[新习合] 点击第 {plan.Position} 位选择按钮：({target[0]},{target[1]})");
            return true;
        }
        catch (MaaStopException)
        {
            LoggerHelper.Info("[新习合] 手动停止目标扫描");
            return false;
        }
        catch (Exception e)
        {
            NewMixTargetSelectionState.Current = new(NewMixTargetSelectionOutcome.Unreadable, 0);
            LoggerHelper.Error($"[新习合] 目标扫描异常：{e.Message}");
            return false;
        }
    }

    private static List<NewMixTargetSlot> ReadSlotsUntilConfirmed<T>(T context) where T : IMaaContext
    {
        var timer = Stopwatch.StartNew();
        while (true)
        {
            ActionParamHelper.ThrowIfStopping(context);
            try
            {
                var slots = ReadSlots(context, timer);
                if (timer.ElapsedMilliseconds >= ScanTimeoutMs)
                    throw new TimeoutException("习合目标扫描超过 20 秒");
                return slots;
            }
            catch (InvalidOperationException e)
            {
                var remaining = ScanTimeoutMs - timer.ElapsedMilliseconds;
                if (remaining <= 0)
                    throw new TimeoutException($"习合目标扫描超过 20 秒：{e.Message}", e);

                LoggerHelper.Warning($"[新习合] {e.Message}，重新扫描");
                ActionParamHelper.SleepWithStopCheck(context, (int)Math.Min(ScanIntervalMs, remaining));
            }
        }
    }

    private static List<NewMixTargetSlot> ReadSlots<T>(T context, Stopwatch timer) where T : IMaaContext
    {
        var slots = new List<NewMixTargetSlot>(MarkerRois.Length);
        using var image = context.GetImage();
        using var bitmap = image?.ToBitmap();
        if (bitmap == null)
            throw new InvalidOperationException("无法获取习合目标列表截图");

        for (var index = 0; index < MarkerRois.Length; index++)
        {
            ActionParamHelper.ThrowIfStopping(context);
            // 同一帧内正向确认锁定、未上锁或空位，避免把过渡画面当作刀剑。
            var isLocked = ContainsColor(bitmap, MarkerRois[index], LockedR, LockedG, LockedB, ColorTolerance);
            var isUnlocked = ContainsGray(bitmap, MarkerRois[index], UnlockedLower, UnlockedUpper);
            var isEmpty = ContainsGray(bitmap, MarkerRois[index], EmptyLower, EmptyUpper);
            if ((isLocked ? 1 : 0) + (isUnlocked ? 1 : 0) + (isEmpty ? 1 : 0) != 1)
                throw new InvalidOperationException($"第 {index + 1} 位标记颜色无法确认：上锁={isLocked}，未上锁={isUnlocked}，空位={isEmpty}");

            var hasSword = isLocked || isUnlocked;
            int? level = null;
            if (isUnlocked)
            {
                level = TryReadLevel(context, LevelRois[index], timer);
                if (!level.HasValue)
                    throw new InvalidOperationException($"第 {index + 1} 位未上锁刀剑的乱舞等级无法识别");
            }

            slots.Add(new(hasSword, isLocked, level));
            LoggerHelper.Info($"[新习合] 第 {index + 1} 位：有刀={hasSword}，上锁={isLocked}，乱舞={level?.ToString() ?? "未识别"}");
        }

        return slots;
    }

    private static int? TryReadLevel<T>(T context, int[] roi, Stopwatch timer) where T : IMaaContext
    {
        var level = TryReadLevel(context, roi, LevelOcrAttempts, timer);
        if (level.HasValue)
            return level;

        var expandedRoi = new[]
        {
            Math.Max(0, roi[0] - ExpandedLevelOcrMargin),
            Math.Max(0, roi[1] - ExpandedLevelOcrMargin),
            roi[2] + ExpandedLevelOcrMargin * 2,
            roi[3] + ExpandedLevelOcrMargin * 2
        };
        LoggerHelper.Warning($"[新习合] 乱舞等级常规 OCR 未命中，尝试扩大区域：[{expandedRoi[0]},{expandedRoi[1]},{expandedRoi[2]},{expandedRoi[3]}]");
        return TryReadLevel(context, expandedRoi, LevelOcrAttempts, timer);
    }

    private static int? TryReadLevel<T>(T context, int[] roi, int attempts, Stopwatch timer) where T : IMaaContext
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            ActionParamHelper.ThrowIfStopping(context);
            if (timer.ElapsedMilliseconds >= ScanTimeoutMs)
                return null;
            using var image = context.GetImage();
            if (image != null)
            {
                var text = context.GetText(roi[0], roi[1], roi[2], roi[3], image);
                var match = Regex.Match(text ?? string.Empty, @"\d+");
                if (int.TryParse(match.Value, out var level))
                    return level;
            }

            if (attempt < attempts - 1)
                ActionParamHelper.SleepWithStopCheck(context, (int)Math.Min(ScanIntervalMs, Math.Max(0, ScanTimeoutMs - timer.ElapsedMilliseconds)));
        }

        return null;
    }

    private static bool ContainsGray(Bitmap bitmap, int[] roi, byte lower, byte upper)
    {
        return ContainsColor(bitmap, roi, lower, lower, lower, upper, upper, upper);
    }

    private static bool ContainsColor(Bitmap bitmap, int[] roi, byte red, byte green, byte blue, byte tolerance)
    {
        return ContainsColor(
            bitmap,
            roi,
            (byte)Math.Max(0, red - tolerance),
            (byte)Math.Max(0, green - tolerance),
            (byte)Math.Max(0, blue - tolerance),
            (byte)Math.Min(255, red + tolerance),
            (byte)Math.Min(255, green + tolerance),
            (byte)Math.Min(255, blue + tolerance));
    }

    private static bool ContainsColor(Bitmap bitmap, int[] roi, byte lowerR, byte lowerG, byte lowerB, byte upperR, byte upperG, byte upperB)
    {
        if (roi[0] < 0 || roi[1] < 0 || roi[2] <= 0 || roi[3] <= 0
            || roi[0] + roi[2] > bitmap.PixelSize.Width || roi[1] + roi[3] > bitmap.PixelSize.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(roi), "习合目标标记区域超出截图范围");
        }

        var bytes = new byte[roi[2] * roi[3] * 4];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(roi[0], roi[1], roi[2], roi[3]), handle.AddrOfPinnedObject(), bytes.Length, roi[2] * 4);
        }
        finally
        {
            handle.Free();
        }

        for (var index = 0; index < bytes.Length; index += 4)
        {
            var blue = bytes[index];
            var green = bytes[index + 1];
            var red = bytes[index + 2];
            if (red >= lowerR && red <= upperR && green >= lowerG && green <= upperG && blue >= lowerB && blue <= upperB)
                return true;
        }

        return false;
    }
}
