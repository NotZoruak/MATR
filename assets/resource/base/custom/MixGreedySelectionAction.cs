using Avalonia;
using Avalonia.Media.Imaging;
using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>按精确需求量选择习合素材，尽量避免乱舞经验溢出。</summary>
public sealed class MixGreedySelectionAction : IMaaCustomAction
{
    // 素材行右侧的竖直边框不受刀名、头像和勾选图标影响，滚动后重新定位各行。
    private const int MaterialEdgeX = 1099;
    private const int MaterialListTop = 124;
    private const int MaterialListHeight = 554;
    private const int MaterialClickX = 858;
    private const int MinimumMaterialRowHeight = 80;
    private const int MaximumMaterialRowHeight = 98;
    private const int SelectionStateAttempts = 12;
    private const int SelectionStateIntervalMilliseconds = 100;

    private static readonly int[] SelectAll = [1145, 460, 111, 40];
    private static readonly int[] ClearAllSelection = [1151, 361, 101, 30];
    /// <summary>素材列表上滑坐标：x, 起点 y, x, 终点 y</summary>
    private static readonly int[] MaterialPageScroll = [700, 617, 700, 212];

    private const int RarityX = 246;
    private const int RarityY = 211;
    private static readonly int[] RarityNameRoi = [293, 240, 140, 28];
    private static readonly int[] RarityRecoveryClickRegion = [1269, 124, 8, 7];
    private const int RarityRecoveryClickLimit = 5;
    private const int RarityRecoveryClickIntervalMilliseconds = 500;
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

            ClickRegion(context, SelectAll, "一键选择", 0);
            if (!WaitForSelectedCount(context, count => count > 0, out var selected))
            {
                if (selected == 0 && TryReadMaterialRows(context, out var rows))
                {
                    var available = rows.Count(row => !row.IsSelected);
                    LoggerHelper.Warning(rows.Any(row => row.IsSelected)
                        ? "[习合] 一键选择后计数持续为0/30，但画面存在已选素材，识别结果不一致，停止选材"
                        : available > 0
                        ? $"[习合] 一键选择后持续为0/30，当前画面仍有{available}行未选素材，选择未生效，停止选材"
                        : "[习合] 一键选择后持续为0/30，未识别到可用的完整素材行，停止选材");
                }
                else
                {
                    LoggerHelper.Warning("[习合] 无法确认一键选择后的有效素材数量，停止选材");
                }
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
            else if (!CancelExcessMaterials(context, required))
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

    /// <summary>全部解除后确认已选数量归零，再按升序逐把选择至目标数量。</summary>
    private static bool ClearAllAndSelectMaterials<T>(T context, int required) where T : IMaaContext
    {
        ClickRegion(context, ClearAllSelection, "全部解除", 0);
        if (WaitForSelectedCount(context, count => count == 0, out var selected))
            return SelectMaterials(context, required);

        LoggerHelper.Warning($"[习合] 全部解除后未确认数量归零，当前已选{selected}把，停止选材");
        return false;
    }

    /// <summary>按升序逐页选择素材；根据当前画面定位素材行，并逐把核对已选数量。</summary>
    private static bool SelectMaterials<T>(T context, int required) where T : IMaaContext
    {
        for (var page = 0; page <= MaxPageScrolls; page++)
        {
            if (!TryReadSelectedCount(context, out var selectedCount))
            {
                LoggerHelper.Warning("[习合] 无法读取已选素材数量");
                return false;
            }

            if (selectedCount > required)
            {
                LoggerHelper.Warning($"[习合] 已选数量超过预期：{selectedCount}，预期：{required}");
                return false;
            }

            while (selectedCount < required)
            {
                if (!TryReadMaterialRows(context, out var rows))
                    return false;
                var row = rows.FirstOrDefault(item => !item.IsSelected);
                if (row == default)
                    break;

                if (!ChangeSingleMaterial(context, row, selectedCount + 1, out selectedCount))
                    return false;
            }

            if (selectedCount == required)
                return true;
            if (IsAtMaterialBottom(context))
            {
                LoggerHelper.Warning($"[习合] 已到底部，仅选中{selectedCount}把素材，预期{required}把");
                return false;
            }

            if (page == MaxPageScrolls)
                break;
            HoldSwipeToNextMaterialPage(context);
            ActionParamHelper.SleepWithStopCheck(context, MixGreedySelectionDecision.SwipeSettleDelayMilliseconds);
        }

        LoggerHelper.Warning("[习合] 素材列表翻页次数超过上限");
        return false;
    }

    /// <summary>只点击一次素材行，随后读取画面，确认选中状态与数量都发生了预期变化。</summary>
    private static bool ChangeSingleMaterial<T>(T context, MaterialRow row, int expectedCount, out int selectedCount) where T : IMaaContext
    {
        context.ClickWithProgress(MaterialClickX, row.CenterY);
        if (WaitForSelectedCount(context, count => count == expectedCount, out selectedCount)
            && TryReadMaterialRows(context, out var rows)
            && rows.Any(item => item.Top <= row.CenterY && item.Bottom >= row.CenterY
                && item.IsSelected != row.IsSelected))
            return true;

        LoggerHelper.Warning($"[习合] 素材行y={row.CenterY}点击后未确认状态变化，已选{selectedCount}把，预期{expectedCount}把");
        return false;
    }

    /// <summary>逐页取消超额素材；处理当前画面上的已选行后，再判断是否已经到底。</summary>
    private static bool CancelExcessMaterials<T>(T context, int required) where T : IMaaContext
    {
        for (var page = 0; page <= MaxPageScrolls; page++)
        {
            // 翻页动作也可能改变勾选状态，以画面数量作为本页起点。
            if (!TryReadSelectedCount(context, out var selectedCount) || selectedCount < required)
            {
                LoggerHelper.Warning("[习合] 翻页后已选数量异常，停止取消素材");
                return false;
            }
            var cancelCount = selectedCount - required;
            while (cancelCount > 0)
            {
                if (!TryReadMaterialRows(context, out var rows))
                    return false;
                var row = rows.FirstOrDefault(item => item.IsSelected);
                if (row == default)
                    break;

                if (!ChangeSingleMaterial(context, row, selectedCount - 1, out selectedCount))
                    return false;
                cancelCount = selectedCount - required;
            }

            if (cancelCount == 0)
                return true;
            if (IsAtMaterialBottom(context))
            {
                LoggerHelper.Warning($"[习合] 已到底部，仍有{cancelCount}把素材未取消");
                return false;
            }

            if (page == MaxPageScrolls)
                break;
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

    /// <summary>读取当前素材列表的边框色带，定位足够完整的可点击素材行。</summary>
    private static bool TryReadMaterialRows<T>(T context, out List<MaterialRow> rows) where T : IMaaContext
    {
        rows = [];
        using var image = context.GetImage();
        if (image == null)
            return false;
        using var bitmap = image.ToBitmap();
        if (bitmap == null)
            return false;

        if (bitmap.PixelSize.Width <= MaterialEdgeX || bitmap.PixelSize.Height < MaterialListTop + MaterialListHeight)
            return false;

        var column = new byte[MaterialListHeight * 4];
        var handle = GCHandle.Alloc(column, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(MaterialEdgeX, MaterialListTop, 1, MaterialListHeight),
                handle.AddrOfPinnedObject(), column.Length, 4);
        }
        finally
        {
            handle.Free();
        }

        rows = FindMaterialRows(column);
        return true;
    }

    /// <summary>根据竖直色带的连续区间识别素材行，忽略顶部和底部露出的短残行。</summary>
    internal static List<MaterialRow> FindMaterialRows(byte[] column)
    {
        var rows = new List<MaterialRow>();
        var start = 0;
        var previousState = -1;
        var height = column.Length / 4;
        for (var y = 0; y <= height; y++)
        {
            var state = -1;
            if (y < height)
            {
                var offset = y * 4;
                var red = column[offset + 2];
                var green = column[offset + 1];
                var blue = column[offset];
                if (Math.Abs(red - SelectedGreenR) <= SelectedGreenTolerance
                    && Math.Abs(green - SelectedGreenG) <= SelectedGreenTolerance
                    && Math.Abs(blue - SelectedGreenB) <= SelectedGreenTolerance)
                    state = 1;
                else if (red == 251 && green == 250 && blue == 245)
                    state = 0;
            }

            if (state == previousState)
                continue;

            var length = y - start;
            if (previousState >= 0 && length >= MinimumMaterialRowHeight && length <= MaximumMaterialRowHeight)
                rows.Add(new MaterialRow(MaterialListTop + start, MaterialListTop + y - 1, previousState == 1));
            start = y;
            previousState = state;
        }
        return rows;
    }

    /// <summary>素材行在当前截图中的纵向范围及选中状态。</summary>
    internal readonly record struct MaterialRow(int Top, int Bottom, bool IsSelected)
    {
        public int CenterY => (Top + Bottom) / 2;
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

    /// <summary>无法读取稀有度时，尝试点击推进习合演出，每次点击后重新读取。</summary>
    private static bool TryReadRarityAfterScreenReady<T>(T context, out int rarity) where T : IMaaContext
    {
        for (var clicks = 0; ; clicks++)
        {
            ActionParamHelper.ThrowIfStopping(context);
            if (TryReadRarity(context, out rarity) || TryReadRarityFromSwordName(context, out rarity))
                return true;

            if (clicks == RarityRecoveryClickLimit)
                return false;

            ClickRegion(context, RarityRecoveryClickRegion,
                $"稀有度未读到，尝试推进习合演出（{clicks + 1}/{RarityRecoveryClickLimit}）",
                RarityRecoveryClickIntervalMilliseconds);
        }
    }

    /// <summary>取色失败后，使用刀名 OCR 识别已知稀有度的刀剑。</summary>
    private static bool TryReadRarityFromSwordName<T>(T context, out int rarity) where T : IMaaContext
    {
        using var image = context.GetImage();
        if (image == null)
        {
            rarity = 0;
            return false;
        }

        var node = new MaaNode
        {
            Name = "MixRaritySwordNameOcr",
            Recognition = "OCR",
            OnlyRec = true,
            Roi = new List<int>(RarityNameRoi),
        };
        var detail = context.RunRecognition(node, image);
        var query = string.IsNullOrWhiteSpace(detail?.Detail)
            ? null
            : Newtonsoft.Json.JsonConvert.DeserializeObject<MaaExtensions.RecognitionQuery>(detail.Detail);
        var text = query?.Best?.Text ?? string.Empty;

        if (text.Contains("石切丸", StringComparison.Ordinal)
            || text.Contains("太郎太刀", StringComparison.Ordinal)
            || text.Contains("次郎太刀", StringComparison.Ordinal)
            || text.Contains("称称切丸", StringComparison.Ordinal))
        {
            rarity = 3;
            LoggerHelper.Info($"[习合] 刀名 OCR 识别为{text}，按稀有度3处理");
            return true;
        }

        if (text.Contains("萤丸", StringComparison.Ordinal))
        {
            rarity = 4;
            LoggerHelper.Info($"[习合] 刀名 OCR 识别为{text}，按稀有度4处理");
            return true;
        }

        LoggerHelper.Warning($"[习合] 稀有度取色失败，刀名 OCR 未匹配已知刀剑：{text}");
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
            if (TryReadSelectedCountOnce(context, out selected))
                return true;

            if (attempt < 4)
                ActionParamHelper.SleepWithStopCheck(context, 200);
        }

        selected = -1;
        return false;
    }

    /// <summary>只读取一次当前画面中的已选数量，无法识别时返回负数。</summary>
    private static bool TryReadSelectedCountOnce<T>(T context, out int selected) where T : IMaaContext
    {
        using var image = context.GetImage();
        if (image != null)
        {
            var text = context.GetText(SelectedCountX, SelectedCountY, SelectedCountWidth, SelectedCountHeight, image);
            if (MixGreedySelectionDecision.TryParseSelectedCount(text, out selected))
                return true;
        }

        selected = -1;
        return false;
    }

    /// <summary>输入后只观察数量变化，不重复点击，避免旧帧或尚未完成的界面刷新造成误判。</summary>
    private static bool WaitForSelectedCount<T>(T context, Func<int, bool> expected, out int selected) where T : IMaaContext
    {
        selected = -1;
        for (var attempt = 0; attempt < SelectionStateAttempts; attempt++)
        {
            ActionParamHelper.ThrowIfStopping(context);
            if (TryReadSelectedCountOnce(context, out var current))
            {
                selected = current;
                if (expected(current))
                    return true;
            }

            if (attempt < SelectionStateAttempts - 1)
                ActionParamHelper.SleepWithStopCheck(context, SelectionStateIntervalMilliseconds);
        }
        return false;
    }

    /// <summary>点击指定区域中心并等待页面更新。</summary>
    private static void ClickRegion<T>(T context, int[] region, string description, int delayMilliseconds = 200) where T : IMaaContext
    {
        var x = region[0] + region[2] / 2;
        var y = region[1] + region[3] / 2;
        context.ClickWithProgress(x, y);
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
