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
    /// <summary>刀解列表上滑坐标：x, 起点 y, x, 终点 y</summary>
    private static readonly int[] ListScroll = [486, 650, 486, 160];
    private const byte BottomR = 114;
    private const byte BottomG = 113;
    private const byte BottomB = 113;
    private const byte BottomColorTolerance = 1;

    public string Name { get; set; } = nameof(ForgeDisassembleSelectAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            var parameters = ActionParamHelper.Parse(args.ActionParam);
            if ((string?)parameters["mode"] == "clearance")
                return RunClearance(context, args.NodeName);

            if (!ConfigurationManager.Current.TryGetValue(ConfigurationKeys.AllowListSwords, out List<string>? allowList)
                || allowList is not { Count: > 0 })
            {
                LoggerHelper.Warning("[日课 锻刀] 刀解/合成许可名单为空");
                return false;
            }

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

                ListOcrScan.ScrollUp(context, ListScroll);
                ActionParamHelper.SleepWithStopCheck(context, ListOcrScan.ScrollSettleMilliseconds);
            }
        }
        catch (MaaStopException)
        {
            LoggerHelper.Info("[日课 锻刀] 手动停止刀解素材选择");
            return false;
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"[刀解选刀] 刀解素材选择异常：{e.Message}");
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

        var query = ListOcrScan.OcrAll(context, image, SwordNameListRoi);
        var candidates = query?.All
            .Where(item => item.Score >= ListOcrScan.SwordNameMinScore
                && item.Text != null
                && item.Box is { Count: >= 4 }
                && SwordNameMatcher.FindMatchedName(item.Text, allowList) != null)
            .OrderBy(item => item.Box![1])
            .ToList() ?? [];

        foreach (var candidate in candidates.Take(maximumSelectCount))
        {
            ActionParamHelper.ThrowIfStopping(context);
            var box = candidate.Box!;
            var targetX = box[0] + box[2] / 2;
            var targetY = box[1] + box[3] / 2;
            LoggerHelper.Info($"[日课 锻刀] 选择可刀解刀剑「{candidate.Text}」");
            context.ClickWithProgress(targetX, targetY);
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

        var pixelBytes = new byte[4 * BottomMarkerRoi[3]];
        var handle = GCHandle.Alloc(pixelBytes, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(
                new Avalonia.PixelRect(BottomMarkerRoi[0], BottomMarkerRoi[1], BottomMarkerRoi[2], BottomMarkerRoi[3]),
                handle.AddrOfPinnedObject(),
                pixelBytes.Length,
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

    private const int ClearanceListTop = 156;
    private const int ClearanceListHeight = 536;
    /// <summary>清仓每次移动约四行，保留超过一整行重叠，避免边缘残行漏扫。</summary>
    private static readonly int[] ClearanceListScroll = [486, 650, 486, 258];

    /// <summary>清仓模式只选择许可名单内的刀剑，不读取日课腾位数量。</summary>
    private static bool RunClearance<T>(T context, string nodeName) where T : IMaaContext
    {
        if (!ConfigurationManager.Current.TryGetValue(ConfigurationKeys.AllowListSwords, out List<string>? allowList)
            || allowList is not { Count: > 0 })
        {
            LoggerHelper.Info("[刀解清仓] 许可名单为空，正常结束任务");
            return context.OverrideNext(nodeName, ["D_CompleteClearance"]);
        }

        using (var image = GetClearanceImage(context))
        {
            var title = context.GetText(572, 59, 144, 44, image);
            if (title?.Contains("刀解选择", StringComparison.Ordinal) != true)
                throw new InvalidOperationException("当前不是刀解选择画面");
            if (!TryReadSelectedCount(context, image, out var selected))
                throw new InvalidOperationException("无法读取刀解已选数量");
            // 用户可能从手动选刀状态启动，不能把名单外的遗留选择一起提交。
            if (selected > 0)
            {
                context.ClickWithProgress(1200, 378);
                VerifyClearanceSelection(context, 0, null);
            }
        }

        // 沿用日课隐藏保护刀剑的颜色依据，切换后必须确认保护刀剑已隐藏。
        using (var image = GetClearanceImage(context))
        {
            if (context.ColorMatch(172, 21, 21, 178, 27, 27, image, out _,
                    threshold: 1.0, x: 137, y: 141, w: 4, h: 4, count: 1))
                context.ClickWithProgress(137, 141);
        }
        using (var image = GetClearanceImage(context))
        {
            if (context.ColorMatch(172, 21, 21, 178, 27, 27, image, out _,
                    threshold: 1.0, x: 137, y: 141, w: 4, h: 4, count: 1))
                throw new InvalidOperationException("未能隐藏保护中的刀剑");
            ResetClearanceListToTop(context, image);
        }

        byte[]? beforeScroll = null;
        while (true)
        {
            ActionParamHelper.ThrowIfStopping(context);
            using var image = GetClearanceImage(context);
            var column = ReadClearancePixels(image, new Avalonia.PixelRect(78, ClearanceListTop, 1, ClearanceListHeight));
            var selection = ReadClearancePixels(image, new Avalonia.PixelRect(80, ClearanceListTop, 1, ClearanceListHeight));
            var rows = FindClearanceRows(column, selection);
            var atEnd = IsShortClearanceList(column, selection) || IsAtBottom(image);
            if (!TryReadSelectedCount(context, image, out var selected) || selected is < 0 or > 30)
                throw new InvalidOperationException("无法读取有效的刀解已选数量");
            if (selected == 30)
                return context.OverrideNext(nodeName, ["D_ClickDisassembleButton"]);
            var screen = ReadClearancePixels(image, new Avalonia.PixelRect(1115, ClearanceListTop, 1, ClearanceListHeight));
            if (beforeScroll != null && beforeScroll.AsSpan().SequenceEqual(screen) && !atEnd)
                throw new InvalidOperationException("刀剑列表滑动未生效且没有到底依据");

            if (rows.Count == 0 && !IsShortClearanceList(column, selection))
                throw new InvalidOperationException("无法识别刀解列表的完整刀剑行");
            var query = ListOcrScan.OcrAll(context, image, SwordNameListRoi);
            ClearanceRow? target = null;
            string? targetName = null;
            foreach (var row in rows.Where(row => !row.IsSelected))
            {
                // 刀名位于行的下部，只处理完整行，不点击上下边缘的残行。
                var names = query?.All.Where(item => item.Box is { Count: >= 4 }
                        && item.Box[1] >= row.Bottom - 28 && item.Box[1] <= row.Bottom
                        && item.Score >= ListOcrScan.SwordNameMinScore && !string.IsNullOrWhiteSpace(item.Text))
                    .Select(item => item.Text!).ToList() ?? [];
                if (names.Count == 0)
                    throw new InvalidOperationException("完整刀剑行的刀名识别失败，停止清仓");
                targetName = names.Select(name => SwordNameMatcher.FindMatchedName(name, allowList)).FirstOrDefault(name => name != null);
                if (targetName == null)
                    continue;
                target = row;
                break;
            }

            if (target is { } hit)
            {
                LoggerHelper.Info($"[刀解清仓] 选择「{targetName}」，当前已选 {selected}/30");
                context.ClickWithProgress(1047, hit.CenterY);
                VerifyClearanceSelection(context, selected + 1, hit);
                // 每次选择后重新识别整页，不能复用点击前的坐标批量操作。
                beforeScroll = null;
                continue;
            }

            if (atEnd)
            {
                if (selected > 0)
                    return context.OverrideNext(nodeName, ["D_ClickDisassembleButton"]);
                var capacity = context.GetText(962, 68, 120, 29, image);
                if (!TryReadRatio(capacity, out var current, out var maximum))
                    throw new InvalidOperationException($"无法读取所持刀剑容量：{capacity}");
                var route = ResolveClearanceRoute(selected, current, maximum);
                LoggerHelper.Info(route == "D_CompleteClearance"
                    ? "[刀解清仓] 没有许可刀剑且刀位已满，正常结束任务"
                    : $"[刀解清仓] 没有许可刀剑，剩余 {maximum - current} 个刀位，前往领取邮箱");
                return context.OverrideNext(nodeName, [route]);
            }

            beforeScroll = screen;
            ListOcrScan.ScrollUp(context, ClearanceListScroll);
        }
    }

    /// <summary>只观察结果，不重复输入；数量和目标行状态必须同时确认。</summary>
    private static void VerifyClearanceSelection<T>(T context, int expected, ClearanceRow? target) where T : IMaaContext
    {
        for (var observation = 0; observation < 12; observation++)
        {
            using var image = GetClearanceImage(context);
            if (TryReadSelectedCount(context, image, out var selected) && selected == expected)
            {
                var column = ReadClearancePixels(image, new Avalonia.PixelRect(78, ClearanceListTop, 1, ClearanceListHeight));
                var selection = ReadClearancePixels(image, new Avalonia.PixelRect(80, ClearanceListTop, 1, ClearanceListHeight));
                var rows = FindClearanceRows(column, selection);
                if (target == null ? rows.All(row => !row.IsSelected)
                    : rows.Any(row => row.Top == target.Value.Top && row.IsSelected))
                    return;
            }
            ActionParamHelper.SleepWithStopCheck(context, 100);
        }
        throw new InvalidOperationException("刀剑选择状态或数量未按预期变化");
    }

    /// <summary>定位现有滚动条滑块，执行一次回顶手势后直接进入选刀。</summary>
    private static void ResetClearanceListToTop<T>(T context, IMaaImageBuffer image) where T : IMaaContext
    {
        var column = ReadClearancePixels(image, new Avalonia.PixelRect(78, ClearanceListTop, 1, ClearanceListHeight));
        var selection = ReadClearancePixels(image, new Avalonia.PixelRect(80, ClearanceListTop, 1, ClearanceListHeight));
        if (IsShortClearanceList(column, selection))
            return;
        var thumb = ReadClearancePixels(image, new Avalonia.PixelRect(1115, ClearanceListTop, 1, ClearanceListHeight));
        var top = FindClearanceThumbTop(thumb);
        if (top < 0)
            throw new InvalidOperationException("无法定位刀解列表滚动条");
        if (top <= 4)
            return;
        context.SwipeWithProgress(1115, ClearanceListTop + top + 5, 1115, ClearanceListTop + 6, 500);
    }

    private static int FindClearanceThumbTop(byte[] column)
    {
        for (var y = 0; y < column.Length / 4 - 8; y++)
            if (Enumerable.Range(y, 8).All(index => IsClearanceColor(column, index, 243, 243, 242)))
                return y;
        return -1;
    }

    /// <summary>直接获取当前画面，不要求连续帧一致；点击是否生效由选中状态和数量确认。</summary>
    private static IMaaImageBuffer GetClearanceImage<T>(T context) where T : IMaaContext
    {
        ActionParamHelper.ThrowIfStopping(context);
        return context.GetImage() ?? throw new InvalidOperationException("获取刀解列表截图失败");
    }

    private static byte[] ReadClearancePixels(IMaaImageBuffer image, Avalonia.PixelRect region)
    {
        using var bitmap = image.ToBitmap() ?? throw new InvalidOperationException("刀解截图无法转为位图");
        if (bitmap.PixelSize.Width != 1280 || bitmap.PixelSize.Height != 720)
            throw new InvalidOperationException("刀解截图必须为 1280×720");
        var pixels = new byte[region.Width * region.Height * 4];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(region, handle.AddrOfPinnedObject(), pixels.Length, region.Width * 4);
        }
        finally
        {
            handle.Free();
        }
        return pixels;
    }

    internal readonly record struct ClearanceRow(int Top, int Bottom, bool IsSelected)
    {
        public int CenterY => (Top + Bottom) / 2;
    }

    /// <summary>以左侧固定外边框定位行，从左侧绿色标记读取选中状态，避开右侧资源闪光。</summary>
    internal static List<ClearanceRow> FindClearanceRows(byte[] column, byte[] selection)
    {
        var rows = new List<ClearanceRow>();
        if (column.Length != ClearanceListHeight * 4 || selection.Length != column.Length)
            return rows;
        var start = -1;
        for (var y = 0; y <= ClearanceListHeight; y++)
        {
            var isBorder = y < ClearanceListHeight && IsClearanceColor(column, y, 175, 172, 162);
            if (isBorder)
            {
                if (start < 0)
                    start = y;
                continue;
            }
            // 完整外边框为87像素，实际刀剑行从外边框上方4像素开始。
            if (start >= 4 && y < ClearanceListHeight && y - start is >= 86 and <= 88)
            {
                var top = ClearanceListTop + start - 4;
                var bottom = ClearanceListTop + y - 2;
                rows.Add(new ClearanceRow(top, bottom,
                    IsClearanceColor(selection, (top + bottom) / 2 - ClearanceListTop, 145, 255, 67)));
            }
            start = -1;
        }
        return rows;
    }

    /// <summary>不足一页或空列表时，列表末端必须有连续空白；未知色带不能当作空列表。</summary>
    internal static bool IsShortClearanceList(byte[] column, byte[] selection)
    {
        if (column.Length != ClearanceListHeight * 4)
            return false;
        var rows = FindClearanceRows(column, selection);
        var last = rows.Count == 0 ? -1 : rows[^1].Bottom - ClearanceListTop;
        if (last >= ClearanceListHeight - 40)
            return false;
        for (var y = last < 0 ? 0 : last + 6; y < ClearanceListHeight; y++)
            if (!IsClearanceColor(column, y, 248, 244, 230))
                return false;
        return true;
    }

    private static bool IsClearanceColor(byte[] column, int y, int red, int green, int blue)
        => Math.Abs(column[y * 4 + 2] - red) <= 1
            && Math.Abs(column[y * 4 + 1] - green) <= 1
            && Math.Abs(column[y * 4] - blue) <= 1;

    internal static bool TryReadRatio(string? text, out int current, out int maximum)
    {
        current = maximum = 0;
        var match = Regex.Match(text ?? string.Empty, @"(?<current>\d+)\s*/\s*(?<maximum>\d+)");
        return match.Success && int.TryParse(match.Groups["current"].Value, out current)
            && int.TryParse(match.Groups["maximum"].Value, out maximum)
            && maximum > 0 && current <= maximum;
    }

    internal static string ResolveClearanceRoute(int selected, int current, int maximum)
        => selected > 0 ? "D_ClickDisassembleButton"
            : current < maximum ? "D_OpenMenuForMailbox" : "D_CompleteClearance";
}
