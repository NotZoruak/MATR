using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Helper;
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>日课锻刀的刀位容量与待收取刀剑数量计算结果。</summary>
public static class DailyTaskForgeContext
{
    public static int PendingSwordCount { get; private set; }
    public static int AvailableSwordSlots { get; private set; }
    public static int RequiredDisassemblyCount { get; private set; }

    internal static void Update(int pendingSwordCount, int availableSwordSlots)
    {
        PendingSwordCount = pendingSwordCount;
        AvailableSwordSlots = availableSwordSlots;
        RequiredDisassemblyCount = Math.Max(0, pendingSwordCount - availableSwordSlots);
    }
}

/// <summary>识别锻刀状况页的待收取刀剑数量与空余刀位，并计算收取前所需刀解数量。</summary>
public class ForgeCapacityCheckAction : IMaaCustomAction
{
    private static readonly int[] SwordCapacityRoi = [845, 68, 261, 29];
    private static readonly int[][] ForgeStatusRois =
    [
        [205, 181, 167, 51],
        [205, 318, 168, 52],
        [204, 452, 169, 53],
        [204, 586, 169, 53],
    ];

    public string Name { get; set; } = nameof(ForgeCapacityCheckAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            ActionParamHelper.ThrowIfStopping(context);
            using var image = context.GetImage();
            if (image == null)
            {
                LoggerHelper.Warning("[日课 锻刀] 获取锻刀状况截图失败");
                return false;
            }

            var capacityText = string.Join(
                " ",
                ListOcrScan.OcrAll(context, image, SwordCapacityRoi)?.All
                    .Where(item => item.Score >= ListOcrScan.MinScore && !string.IsNullOrWhiteSpace(item.Text))
                    .Select(item => item.Text!) ?? []);
            var capacityMatch = Regex.Match(capacityText ?? string.Empty, @"(?<current>\d+)\s*/\s*(?<maximum>\d+)");
            if (!capacityMatch.Success
                || !int.TryParse(capacityMatch.Groups["current"].Value, out var current)
                || !int.TryParse(capacityMatch.Groups["maximum"].Value, out var maximum)
                || current > maximum)
            {
                LoggerHelper.Warning($"[日课 锻刀] 未能识别所持刀剑容量：{capacityText}");
                return false;
            }

            var pending = 0;
            foreach (var roi in ForgeStatusRois)
            {
                var status = context.GetText(roi[0], roi[1], roi[2], roi[3], image) ?? string.Empty;
                if (status.Contains("十连完成", StringComparison.Ordinal))
                    pending += 10;
                else if (status.Contains("完成", StringComparison.Ordinal))
                    pending++;
            }

            var availableSlots = maximum - current;
            DailyTaskForgeContext.Update(pending, availableSlots);
            LoggerHelper.Info(
                $"[日课 锻刀] 待收取={DailyTaskForgeContext.PendingSwordCount}，" +
                $"空余刀位={DailyTaskForgeContext.AvailableSwordSlots}，" +
                $"需刀解={DailyTaskForgeContext.RequiredDisassemblyCount}");
            return availableSlots >= pending;
        }
        catch (MaaStopException)
        {
            LoggerHelper.Info("[日课 锻刀] 手动停止刀位判断");
            return false;
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"[日课 锻刀] 刀位判断异常：{e.Message}");
            return false;
        }
    }
}
