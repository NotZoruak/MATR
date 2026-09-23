using Avalonia;
using Avalonia.Media.Imaging;
using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>按精确需求量选择习合素材，尽量避免乱舞经验溢出。</summary>
public sealed class MixGreedySelectionAction : IMaaCustomAction
{
    private static readonly MixGreedyPoint[] SelectedMaterialMarkers =
    [
        new(1099, 172),
        new(1100, 273),
        new(1100, 374),
        new(1100, 475),
        new(1100, 576)
    ];

    private static readonly int[] SelectAll = [1145, 460, 111, 40];
    private static readonly int[] ClearAllSelection = [1151, 361, 101, 30];
    /// <summary>素材列表上滑坐标：x, 起点 y, x, 终点 y</summary>
    private static readonly int[] MaterialPageScroll = [1100, 617, 1100, 212];

    private const int RarityX = 246;
    private const int RarityY = 211;
    private const int LevelX = 415;
    private const int LevelY = 299;
    private const int LevelWidth = 20;
    private const int LevelHeight = 24;
    private const int NeedX = 427;
    private const int NeedY = 340;
    private const int NeedWidth = 22;
    private const int NeedHeight = 21;
    private const int SelectedCountX = 1148;
    private const int SelectedCountY = 315;
    private const int SelectedCountWidth = 109;
    private const int SelectedCountHeight = 26;
    private const int SelectedGreenR = 145;
    private const int SelectedGreenG = 255;
    private const int SelectedGreenB = 67;
    private const int SelectedGreenTolerance = 2;
    private const int BottomX = 1112;
    private const int BottomY = 686;
    private const int MaxPageScrolls = 20;
    /// <summary>画面处于白屏过渡时，稀有度色值的最大重读次数。</summary>
    private const int ScreenReadyAttempts = 12;
    /// <summary>画面处于白屏过渡时，两次稀有度色值读取之间的间隔毫秒数。</summary>
    private const int ScreenReadyIntervalMilliseconds = 200;

    public string Name { get; set; } = nameof(MixGreedySelectionAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            ActionParamHelper.ThrowIfStopping(context);
            if (!TryReadRarityAfterScreenReady(context, out var rarity)
                || !TryReadInteger(context, LevelX, LevelY, LevelWidth, LevelHeight, out var level)
                || !TryReadNeedForNextLevel(context, out var needForNextLevel))
            {
                LoggerHelper.Warning("[习合] 无法读取稀有度、乱舞等级或下一级需求");
                return false;
            }

            if (level is < 1 or >= 7)
            {
                LoggerHelper.Warning($"[习合] 当前乱舞等级异常或已满级：{level}");
                return false;
            }

            var required = MixGreedySelectionDecision.CalculateRequiredMaterialCount(rarity, level, needForNextLevel);
            LoggerHelper.Info($"[习合] 稀有度{rarity}、乱舞{level}级，升至7级需素材{required}把");

            ClickRegion(context, SelectAll, "一键选择");
            if (!TryReadSelectedCount(context, out var selected))
            {
                LoggerHelper.Warning("[习合] 无法读取一键选择后的已选数量");
                return false;
            }

            var plan = MixGreedySelectionDecision.CreatePlan(required, selected);
            if (plan.Mode == MixMaterialSelectionMode.Proceed)
            {
                if (plan.Difference < 0)
                    LoggerHelper.Info($"[习合] 素材不足：已选{selected}把、目标{required}把，直接习合");
                else
                    LoggerHelper.Info($"[习合] 一键选择已精确选中{selected}把素材");
                return true;
            }

            LoggerHelper.Info($"[习合] 一键选择{selected}把，需要取消{plan.ExcessCount}把");

            if (plan.Mode == MixMaterialSelectionMode.ClearAndReselect)
            {
                LoggerHelper.Info("[习合] 超额超过15把，全部解除后按升序重新选择");
                if (!ClearAllAndSelectMaterials(context, required))
                    return false;
            }
            else if (!CancelExcessMaterials(context, plan.ExcessCount))
            {
                return false;
            }

            if (!TryReadSelectedCount(context, out selected) || selected != required)
            {
                LoggerHelper.Warning($"[习合] 调整后已选数量异常：{selected}，预期：{required}");
                return false;
            }

            LoggerHelper.Info($"[习合] 素材数量已精确调整为{selected}把");
            return true;
        }
        catch (MaaStopException)
        {
            LoggerHelper.Info("[习合] 手动停止葛朗台选材");
            return false;
        }
        catch (Exception exception)
        {
            LoggerHelper.Error($"[习合] 葛朗台选材异常：{exception.Message}");
            return false;
        }
    }

    /// <summary>全部解除后确认首行未选中，再按升序逐把选择至目标数量。</summary>
    private static bool ClearAllAndSelectMaterials<T>(T context, int required) where T : IMaaContext
    {
        for (var attempt = 1; attempt <= MixGreedySelectionDecision.ClearAllAttempts; attempt++)
        {
            ClickRegion(context, ClearAllSelection, "全部解除", 0);
            ActionParamHelper.SleepWithStopCheck(context, MixGreedySelectionDecision.ClearAllDelayMilliseconds);
            if (!IsSelectedMaterial(context, SelectedMaterialMarkers[0]))
                return SelectMaterials(context, required);

            if (attempt < MixGreedySelectionDecision.ClearAllAttempts)
                LoggerHelper.Warning("[习合] 全部解除后第一行素材仍为绿色，重新解除一次");
        }

        LoggerHelper.Warning("[习合] 两次全部解除后第一行素材仍处于选中状态");
        return false;
    }

    /// <summary>按升序逐页选择素材；每页只处理五个完整素材行，数量一律以画面上的「选择中 X/30」为准。</summary>
    private static bool SelectMaterials<T>(T context, int required) where T : IMaaContext
    {
        var expectedCount = -1;
        for (var page = 0; page <= MaxPageScrolls; page++)
        {
            if (!TryReadSelectedCount(context, out var selectedCount))
            {
                LoggerHelper.Warning("[习合] 无法读取已选素材数量");
                return false;
            }

            if (expectedCount >= 0 && selectedCount != expectedCount)
            {
                LoggerHelper.Info($"[习合] 滑动后已选数量由{expectedCount}把变为{selectedCount}把，改按画面数量继续");
            }

            if (selectedCount > required)
            {
                LoggerHelper.Warning($"[习合] 已选数量超过预期：{selectedCount}，预期：{required}");
                return false;
            }

            for (var index = 0; index < SelectedMaterialMarkers.Length && selectedCount < required; index++)
            {
                if (IsSelectedMaterial(context, SelectedMaterialMarkers[index]))
                    continue;

                if (!SelectSingleMaterial(context, index))
                    return false;

                selectedCount++;
            }

            if (selectedCount == required)
                return true;
            if (IsAtMaterialBottom(context))
            {
                LoggerHelper.Warning($"[习合] 已到底部，仅选中{selectedCount}把素材，预期{required}把");
                return false;
            }

            expectedCount = selectedCount;
            HoldSwipeToNextMaterialPage(context);
            ActionParamHelper.SleepWithStopCheck(context, MixGreedySelectionDecision.SwipeSettleDelayMilliseconds);
        }

        LoggerHelper.Warning("[习合] 素材列表翻页次数超过上限");
        return false;
    }

    /// <summary>点击指定行选择素材，并确认该行已经变为选中状态。</summary>
    private static bool SelectSingleMaterial<T>(T context, int index) where T : IMaaContext
    {
        var selectPosition = MixGreedySelectionDecision.CancelPositions[index];
        for (var attempt = 1; attempt <= MixGreedySelectionDecision.ManualClickAttempts; attempt++)
        {
            context.Click(selectPosition.X, selectPosition.Y);
            ActionParamHelper.SleepWithStopCheck(context, 200);
            if (IsSelectedMaterial(context, SelectedMaterialMarkers[index]))
                return true;

            LoggerHelper.Warning($"[习合] 第{index + 1}行素材第{attempt}次选择后未出现绿色状态");
        }

        return false;
    }

    /// <summary>逐页取消超额素材；每页只处理五个完整素材行。</summary>
    private static bool CancelExcessMaterials<T>(T context, int cancelCount) where T : IMaaContext
    {
        for (var page = 0; cancelCount > 0 && page <= MaxPageScrolls; page++)
        {
            foreach (var index in Enumerable.Range(0, SelectedMaterialMarkers.Length))
            {
                if (cancelCount == 0)
                    return true;

                if (!IsSelectedMaterial(context, SelectedMaterialMarkers[index]))
                    continue;

                var cancelPosition = MixGreedySelectionDecision.CancelPositions[index];
                context.Click(cancelPosition.X, cancelPosition.Y);
                ActionParamHelper.SleepWithStopCheck(context, 200);
                if (IsSelectedMaterial(context, SelectedMaterialMarkers[index]))
                {
                    LoggerHelper.Warning($"[习合] 第{index + 1}行素材取消未生效");
                    return false;
                }

                cancelCount--;
            }

            if (cancelCount == 0)
                return true;
            if (IsAtMaterialBottom(context))
            {
                LoggerHelper.Warning($"[习合] 已到底部，仍有{cancelCount}把素材未取消");
                return false;
            }

            HoldSwipeToNextMaterialPage(context);
            ActionParamHelper.SleepWithStopCheck(context, MixGreedySelectionDecision.SwipeSettleDelayMilliseconds);
        }

        LoggerHelper.Warning("[习合] 素材列表翻页次数超过上限");
        return false;
    }

    /// <summary>上滑素材列表到下一页：与其它列表共用同一套 L 形手势与等待。</summary>
    private static void HoldSwipeToNextMaterialPage<T>(T context) where T : IMaaContext
    {
        ListOcrScan.ScrollUp(context, MaterialPageScroll);
        ActionParamHelper.SleepWithStopCheck(context, ListOcrScan.ScrollSettleMilliseconds);
    }

    /// <summary>判断指定素材行右侧是否显示已选中的绿色背景。</summary>
    private static bool IsSelectedMaterial<T>(T context, MixGreedyPoint marker) where T : IMaaContext
    {
        using var image = context.GetImage();
        if (image == null)
            return false;
        using var bitmap = image.ToBitmap();
        if (bitmap == null)
            return false;

        var pixel = ReadPixel(bitmap, marker.X, marker.Y);
        return Math.Abs(pixel.R - SelectedGreenR) <= SelectedGreenTolerance
            && Math.Abs(pixel.G - SelectedGreenG) <= SelectedGreenTolerance
            && Math.Abs(pixel.B - SelectedGreenB) <= SelectedGreenTolerance;
    }

    /// <summary>判断素材列表是否已滑到底部。</summary>
    private static bool IsAtMaterialBottom<T>(T context) where T : IMaaContext
    {
        using var image = context.GetImage();
        if (image == null)
            return true;
        using var bitmap = image.ToBitmap();
        if (bitmap == null)
            return true;

        var pixel = ReadPixel(bitmap, BottomX, BottomY);
        return pixel.R is >= 114 and <= 116
            && pixel.G is >= 113 and <= 115
            && pixel.B is >= 113 and <= 115;
    }

    /// <summary>读取稀有度对应的固定像素颜色。</summary>
    private static bool TryReadRarity<T>(T context, out int rarity) where T : IMaaContext
    {
        using var image = context.GetImage();
        if (image == null)
        {
            rarity = 0;
            return false;
        }
        using var bitmap = image.ToBitmap();
        if (bitmap == null)
        {
            rarity = 0;
            return false;
        }

        var pixel = ReadPixel(bitmap, RarityX, RarityY);
        return MixGreedySelectionDecision.TryGetRarity(pixel.R, pixel.G, pixel.B, out rarity);
    }

    /// <summary>习合完成返回选刀画面时会短暂出现白屏过渡，整帧被白色蒙层冲淡，色值读取必然失败；此时等待画面恢复后再读。</summary>
    private static bool TryReadRarityAfterScreenReady<T>(T context, out int rarity) where T : IMaaContext
    {
        for (var attempt = 1; attempt <= ScreenReadyAttempts; attempt++)
        {
            ActionParamHelper.ThrowIfStopping(context);
            if (TryReadRarity(context, out rarity))
            {
                if (attempt > 1)
                    LoggerHelper.Info($"[习合] 画面已恢复，第 {attempt} 次读取到稀有度 {rarity}");
                return true;
            }

            if (attempt == 1)
                LoggerHelper.Info("[习合] 稀有度色值异常，画面可能处于白屏过渡，等待画面恢复");

            if (attempt < ScreenReadyAttempts)
                ActionParamHelper.SleepWithStopCheck(context, ScreenReadyIntervalMilliseconds);
        }

        rarity = 0;
        return false;
    }

    /// <summary>读取仅包含一个正整数的 OCR 区域。</summary>
    private static bool TryReadInteger<T>(T context, int x, int y, int width, int height, out int value) where T : IMaaContext
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            ActionParamHelper.ThrowIfStopping(context);
            using var image = context.GetImage();
            if (image != null)
            {
                var text = context.GetText(x, y, width, height, image);
                var match = Regex.Match(text ?? string.Empty, @"\d+");
                if (int.TryParse(match.Value, out value) && value > 0)
                    return true;
            }

            if (attempt < 4)
                ActionParamHelper.SleepWithStopCheck(context, 200);
        }

        value = 0;
        return false;
    }

    /// <summary>优先读取常规需求区域，失败后使用更紧凑的备用区域重试。</summary>
    private static bool TryReadNeedForNextLevel<T>(T context, out int value) where T : IMaaContext
    {
        if (TryReadInteger(context, NeedX, NeedY, NeedWidth, NeedHeight, out value))
            return true;

        var fallbackRoi = MixGreedySelectionDecision.FallbackNeedOcrRoi;
        LoggerHelper.Info("[习合] 常规下一级需求 OCR 未命中，尝试备用区域");
        return TryReadInteger(context, fallbackRoi.X, fallbackRoi.Y, fallbackRoi.Width, fallbackRoi.Height, out value);
    }

    /// <summary>读取“已选数量/30”形式的 OCR 结果。</summary>
    private static bool TryReadSelectedCount<T>(T context, out int selected) where T : IMaaContext
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            ActionParamHelper.ThrowIfStopping(context);
            using var image = context.GetImage();
            if (image != null)
            {
                var text = context.GetText(SelectedCountX, SelectedCountY, SelectedCountWidth, SelectedCountHeight, image);
                if (MixGreedySelectionDecision.TryParseSelectedCount(text, out selected))
                    return true;
            }

            if (attempt < 4)
                ActionParamHelper.SleepWithStopCheck(context, 200);
        }

        selected = 0;
        return false;
    }

    /// <summary>点击指定区域中心并等待页面更新。</summary>
    private static void ClickRegion<T>(T context, int[] region, string description, int delayMilliseconds = 200) where T : IMaaContext
    {
        var x = region[0] + region[2] / 2;
        var y = region[1] + region[3] / 2;
        context.Click(x, y);
        LoggerHelper.Info($"[习合] {description}：({x},{y})");
        if (delayMilliseconds > 0)
            ActionParamHelper.SleepWithStopCheck(context, delayMilliseconds);
    }

    /// <summary>读取位图中的单个 RGB 像素。</summary>
    private static RgbColor ReadPixel(Bitmap bitmap, int x, int y)
    {
        if (x < 0 || y < 0 || x >= bitmap.PixelSize.Width || y >= bitmap.PixelSize.Height)
            return default;

        var bytes = new byte[4];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(x, y, 1, 1), handle.AddrOfPinnedObject(), bytes.Length, 4);
        }
        finally
        {
            handle.Free();
        }

        return new RgbColor(bytes[2], bytes[1], bytes[0]);
    }

    private readonly record struct RgbColor(byte R, byte G, byte B);
}
