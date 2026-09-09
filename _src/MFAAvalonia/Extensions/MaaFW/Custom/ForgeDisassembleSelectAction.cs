using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Configuration;
using MFAAvalonia.Helper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>按刀解/合成许可名单在刀解列表中逐页选择刀剑。</summary>
public class ForgeDisassembleSelectAction : IMaaCustomAction
{
    private static readonly int[] SwordNameListRoi = [125, 155, 260, 535];
    private static readonly int[] SelectedCountRoi = [1140, 313, 125, 29];
    private static readonly int[] BottomMarkerRoi = [1115, 692, 1, 2];
    private const byte BottomR = 114;
    private const byte BottomG = 113;
    private const byte BottomB = 113;
    private const byte BottomColorTolerance = 1;

    public string Name { get; set; } = nameof(ForgeDisassembleSelectAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            if (!ConfigurationManager.Current.TryGetValue(ConfigurationKeys.AllowListSwords, out List<string>? allowList)
                || allowList is not { Count: > 0 })
            {
                LoggerHelper.Warning("[日课 锻刀] 刀解/合成许可名单为空");
                return false;
            }

            var parameters = ActionParamHelper.Parse(args.ActionParam);
            var minimumCount = Math.Max(0, parameters["minimum_count"]?.ToObject<int>() ?? 0);
            var requiredCount = Math.Max(DailyTaskForgeContext.RequiredDisassemblyCount, minimumCount);
            while (true)
            {
                ActionParamHelper.ThrowIfStopping(context);
                using var image = context.GetImage();
                if (image == null)
                {
                    LoggerHelper.Warning("[日课 锻刀] 获取刀解列表截图失败");
                    return false;
                }

                if (!TryReadSelectedCount(context, image, out var selectedCount))
                {
                    LoggerHelper.Warning("[日课 锻刀] 未能识别已选刀剑数量");
                    return false;
                }

                LoggerHelper.Info($"[日课 刀解锻刀] 当前已选择 {selectedCount}/30，把需刀解数量为 {requiredCount}");
                if (selectedCount >= requiredCount || selectedCount == 30)
                    return true;

                SelectAllowedSwordsOnCurrentPage(context, allowList, requiredCount - selectedCount);

                using var updatedImage = context.GetImage();
                if (updatedImage == null)
                {
                    LoggerHelper.Warning("[日课 锻刀] 获取刀解列表截图失败");
                    return false;
                }

                if (!TryReadSelectedCount(context, updatedImage, out selectedCount))
                {
                    LoggerHelper.Warning("[日课 锻刀] 未能识别已选刀剑数量");
                    return false;
                }

                LoggerHelper.Info($"[日课 刀解锻刀] 当前已选择 {selectedCount}/30，把需刀解数量为 {requiredCount}");
                if (selectedCount >= requiredCount || selectedCount == 30)
                    return true;

                if (IsAtBottom(updatedImage))
                {
                    LoggerHelper.Warning("日课 锻刀 刀位不足 可刀解刀剑不足");
                    return false;
                }

                ScrollDown(context);
            }
        }
        catch (MaaStopException)
        {
            LoggerHelper.Info("[日课 锻刀] 手动停止刀解素材选择");
            return false;
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"[日课 锻刀] 刀解素材选择异常：{e.Message}");
            return false;
        }
    }

    /// <summary>在当前页的许可名单内选择不超过所需数量的刀剑。</summary>
    private static void SelectAllowedSwordsOnCurrentPage<T>(T context, IReadOnlyCollection<string> allowList, int maximumSelectCount) where T : IMaaContext
    {
        if (maximumSelectCount <= 0)
            return;

        using var image = context.GetImage();
        if (image == null)
            throw new InvalidOperationException("获取刀解列表截图失败");

        var query = FormationScan.OcrAll(context, image, SwordNameListRoi);
        var candidates = query?.All
            .Where(item => item.Score >= FormationScan.MinScore
                && item.Text != null
                && item.Box is { Count: >= 4 }
                && allowList.Any(name => item.Text.Contains(name, StringComparison.Ordinal)))
            .OrderBy(item => item.Box![1])
            .ToList() ?? [];

        foreach (var candidate in candidates.Take(maximumSelectCount))
        {
            ActionParamHelper.ThrowIfStopping(context);
            var box = candidate.Box!;
            var targetX = box[0] + box[2] / 2;
            var targetY = box[1] + box[3] / 2;
            LoggerHelper.Info($"[日课 锻刀] 选择可刀解刀剑「{candidate.Text}」");
            context.Click(targetX, targetY);
            ActionParamHelper.SleepWithStopCheck(context, 500);
        }
    }

    /// <summary>读取右侧“选择中”区域的已选数量。</summary>
    private static bool TryReadSelectedCount<T>(T context, IMaaImageBuffer image, out int selectedCount) where T : IMaaContext
    {
        var text = context.GetText(SelectedCountRoi[0], SelectedCountRoi[1], SelectedCountRoi[2], SelectedCountRoi[3], image);
        var match = Regex.Match(text ?? string.Empty, @"(?<selected>\d+)\s*/\s*30");
        return int.TryParse(match.Groups["selected"].Value, out selectedCount);
    }

    /// <summary>检测滚动条底部标记颜色，判断是否已经滑至列表末尾。</summary>
    private static bool IsAtBottom(IMaaImageBuffer image)
    {
        if (image is not MaaImageBuffer imageBuffer)
            return false;

        using var bitmap = imageBuffer.ToBitmap();
        if (bitmap == null)
            return false;

        var pixelBytes = new byte[4];
        var handle = GCHandle.Alloc(pixelBytes, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(
                new Avalonia.PixelRect(BottomMarkerRoi[0], BottomMarkerRoi[1], BottomMarkerRoi[2], BottomMarkerRoi[3]),
                handle.AddrOfPinnedObject(),
                pixelBytes.Length * BottomMarkerRoi[3],
                4);
        }
        finally
        {
            handle.Free();
        }

        var b = pixelBytes[0];
        var g = pixelBytes[1];
        var r = pixelBytes[2];
        return Math.Abs(r - BottomR) <= BottomColorTolerance
            && Math.Abs(g - BottomG) <= BottomColorTolerance
            && Math.Abs(b - BottomB) <= BottomColorTolerance;
    }

    /// <summary>连续按住 1.5 秒向下滑动刀解列表。</summary>
    private static void ScrollDown<T>(T context) where T : IMaaContext
    {
        const int x = 486;
        const int startY = 650;
        const int endY = 160;
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
