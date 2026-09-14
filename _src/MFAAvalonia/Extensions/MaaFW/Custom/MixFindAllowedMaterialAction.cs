using Avalonia;
using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Configuration;
using MFAAvalonia.Helper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>在合成素材列表中查找许可名单内且未上锁的第一把刀剑，并选中后确认。</summary>
public class MixFindAllowedMaterialAction : IMaaCustomAction
{
    private static readonly int[] ListRoi = [437, 185, 241, 437];
    private const int MaxSwipes = 10;
    private const int LockOffsetX = -9;
    private const int SelectOffsetX = 614;
    private const byte AvailableR = 85;
    private const byte AvailableG = 83;
    private const byte AvailableB = 83;
    private const byte ColorTolerance = 3;

    public string Name { get; set; } = nameof(MixFindAllowedMaterialAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            if (!ConfigurationManager.Current.TryGetValue(ConfigurationKeys.AllowListSwords, out List<string>? allowList)
                || allowList is not { Count: > 0 })
            {
                LoggerHelper.Warning("[日课 合成] 刀解/合成许可名单为空");
                return false;
            }

            for (var swipeCount = 0; swipeCount <= MaxSwipes; swipeCount++)
            {
                ActionParamHelper.ThrowIfStopping(context);
                if (TrySelectAllowedMaterial(context, allowList))
                {
                    LoggerHelper.Info($"[日课 合成] 第 {swipeCount} 次滑动后找到可用素材");
                    ActionParamHelper.SleepWithStopCheck(context, 200);
                    context.Click(1201, 629);
                    return true;
                }

                if (swipeCount == MaxSwipes)
                    break;

                LoggerHelper.Info($"[日课 合成] 当前页面未找到可用素材，滑动列表 ({swipeCount + 1}/{MaxSwipes})");
                ScrollUp(context);
            }

            LoggerHelper.Warning("[日课 合成] 十次滑动后仍未找到可用素材");
            return false;
        }
        catch (MaaStopException)
        {
            LoggerHelper.Info("[日课 合成] 手动停止素材选择");
            return false;
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"[日课 合成] 素材选择异常：{e.Message}");
            return false;
        }
    }

    /// <summary>扫描当前可见列表，选择第一个命中许可名单且未上锁的刀剑。</summary>
    private static bool TrySelectAllowedMaterial<T>(T context, IReadOnlyCollection<string> allowList) where T : IMaaContext
    {
        using var image = context.GetImage();
        if (image == null)
            return false;

        var query = ListOcrScan.OcrAll(context, image, ListRoi);
        var candidates = query?.All
            .Where(item => item.Score >= ListOcrScan.MinScore
                && item.Text != null
                && item.Box is { Count: >= 4 }
                && SwordNameMatcher.FindMatchedName(item.Text, allowList) != null)
            .OrderBy(item => item.Box![1])
            .ToList() ?? [];

        if (candidates.Count == 0)
            return false;

        if (image is not MaaImageBuffer imageBuffer)
            return false;

        using var bitmap = imageBuffer.ToBitmap();
        if (bitmap == null)
            return false;

        foreach (var candidate in candidates)
        {
            var box = candidate.Box!;
            var lockX = box[0] + LockOffsetX;
            var lockY = box[1];
            if (!IsAvailable(bitmap, lockX, lockY))
            {
                LoggerHelper.Info($"[日课 合成] 「{candidate.Text}」已上锁，跳过");
                continue;
            }

            var selectX = box[0] + SelectOffsetX;
            LoggerHelper.Info($"[日课 合成] 选择许可素材「{candidate.Text}」，点击 ({selectX},{box[1]})");
            context.Click(selectX, box[1]);
            return true;
        }

        return false;
    }

    /// <summary>读取刀剑名称左上方偏移像素，颜色符合时表示该刀剑未上锁且可选。</summary>
    private static bool IsAvailable(Avalonia.Media.Imaging.Bitmap bitmap, int x, int y)
    {
        if (x < 0 || y < 0 || x >= bitmap.PixelSize.Width || y >= bitmap.PixelSize.Height)
            return false;

        var pixelBytes = new byte[4];
        var handle = GCHandle.Alloc(pixelBytes, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(x, y, 1, 1), handle.AddrOfPinnedObject(), pixelBytes.Length, 4);
        }
        finally
        {
            handle.Free();
        }

        var b = pixelBytes[0];
        var g = pixelBytes[1];
        var r = pixelBytes[2];
        return Math.Abs(r - AvailableR) <= ColorTolerance
            && Math.Abs(g - AvailableG) <= ColorTolerance
            && Math.Abs(b - AvailableB) <= ColorTolerance;
    }

    /// <summary>连续按住 1.5 秒完成素材列表上滑。</summary>
    private static void ScrollUp<T>(T context) where T : IMaaContext
    {
        const int x = 802;
        const int startY = 634;
        const int endY = 127;
        const int steps = 20;

        context.TouchDown(0, x, startY, 1);
        try
        {
            ActionParamHelper.SleepWithStopCheck(context, 500);
            for (var step = 1; step <= steps; step++)
            {
                var y = startY + (endY - startY) * step / steps;
                context.TouchMove(0, x, y, 1);
                ActionParamHelper.SleepWithStopCheck(context, 500 / steps);
            }
            ActionParamHelper.SleepWithStopCheck(context, 500);
        }
        finally
        {
            context.TouchUp(0);
        }
    }
}
