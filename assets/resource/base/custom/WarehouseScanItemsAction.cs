using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>扫描所持道具页面，并通过上滑遍历所有物品卡片。</summary>
public sealed class WarehouseScanItemsAction : IMaaCustomAction
{
    private const int MaxPages = 100;
    private const int SamePageLimit = 3;
    /// <summary>到列表底部后复读最后一页前的等待：只在整轮扫描结束时执行一次，保留较大余量确保最后一页完整</summary>
    private const int BottomRefillWaitMilliseconds = 1000;
    /// <summary>所持道具列表上滑坐标：x, 起点 y, x, 终点 y</summary>
    private static readonly int[] ScrollCoordinates = [656, 567, 656, 177];
    private static readonly int[] ScrollbarEndRoi = [1239, 672, 3, 2];

    private static readonly int[][] DefaultNameRois =
    [
        [64, 143, 540, 36], [655, 143, 540, 36],
        [64, 336, 540, 36], [655, 336, 540, 36],
        [64, 529, 540, 36], [655, 529, 540, 36],
    ];

    private static readonly int[][] DefaultCountRois =
    [
        [170, 275, 100, 43], [760, 275, 100, 43],
        [170, 468, 100, 43], [760, 468, 100, 43],
        [170, 661, 100, 43], [760, 661, 100, 43],
    ];

    // 滚动到底后，卡片会整体上移，最后两行使用独立的 OCR 区域。
    private static readonly int[][] FinalPageNameRois =
    [
        [64, 280, 540, 36], [655, 280, 540, 36],
        [64, 475, 540, 36], [655, 475, 540, 36],
    ];

    private static readonly int[][] FinalPageCountRois =
    [
        [170, 425, 100, 38], [760, 425, 100, 38],
        [170, 620, 100, 38], [760, 620, 100, 38],
    ];
    private static readonly int[] DefaultKobanRoi = [977, 32, 189, 48];

    public string Name { get; set; } = nameof(WarehouseScanItemsAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            var json = ActionParamHelper.Parse(args.ActionParam);
            var nameRois = ParseRois(json["name_rois"] as JArray, DefaultNameRois);
            var countRois = ParseRois(json["count_rois"] as JArray, DefaultCountRois);
            var kobanRoi = ParseRoi(json["koban_roi"] as JArray, DefaultKobanRoi);
            var draftPath = Path.Combine(AppPaths.ConfigDirectory, "warehouse_scan.json");
            var savedItemNames = WarehouseScanDraftService.LoadSavedOtherItemNames();
            WarehouseScanDraftService.ClearOtherItems(draftPath);
            ReadKoban(context, kobanRoi, draftPath);
            var previousSignature = string.Empty;
            var unchangedPages = 0;
            var recognizedOrder = new List<string>();
            var recognizedNames = new HashSet<string>(StringComparer.Ordinal);
            var reachedBottom = false;

            for (var page = 0; page < MaxPages; page++)
            {
                ActionParamHelper.ThrowIfStopping(context);
                var visibleItems = ReadVisibleItems(context, nameRois, countRois, savedItemNames);
                foreach (var item in visibleItems)
                {
                    WarehouseScanDraftService.UpdateOtherItem(draftPath, item.Name, item.Count);
                    if (recognizedNames.Add(item.Name))
                        recognizedOrder.Add(item.Name);
                    LoggerHelper.Info($"[仓库识别] 其他物品 {item.Name}：{item.Count}");
                }

                if (reachedBottom)
                {
                    // 到底后的首帧可能仍处于滚动动画的最后阶段，再等待一次并复读，确保最后一页完整进入识别区域。
                    ActionParamHelper.SleepWithStopCheck(context, BottomRefillWaitMilliseconds);
                    var finalItems = ReadVisibleItems(context, FinalPageNameRois, FinalPageCountRois, savedItemNames);
                    foreach (var item in finalItems)
                    {
                        WarehouseScanDraftService.UpdateOtherItem(draftPath, item.Name, item.Count);
                        if (recognizedNames.Add(item.Name))
                            recognizedOrder.Add(item.Name);
                        LoggerHelper.Info($"[仓库识别] 所持道具最后一页 {item.Name}：{item.Count}");
                    }
                    CompleteScan(draftPath, recognizedOrder);
                    return true;
                }

                var signature = string.Join("|", visibleItems.Select(item => $"{item.Name}={item.Count}"));
                if (visibleItems.Count == 0)
                    unchangedPages = 0;
                else if (string.Equals(signature, previousSignature, StringComparison.Ordinal))
                    unchangedPages++;
                else
                    unchangedPages = 0;

                LoggerHelper.Info($"[仓库识别] 所持道具页面扫描：第 {page + 1} 页，识别 {visibleItems.Count} 项，连续未变化 {unchangedPages} 次");
                if (unchangedPages >= SamePageLimit)
                {
                    CompleteScan(draftPath, recognizedOrder);
                    return true;
                }

                previousSignature = signature;
                ListOcrScan.ScrollUp(context, ScrollCoordinates);
                ActionParamHelper.SleepWithStopCheck(context, ListOcrScan.ScrollSettleMilliseconds);
                if (IsAtBottom(context))
                    reachedBottom = true;
            }

            CompleteScan(draftPath, recognizedOrder);
            LoggerHelper.Warning($"[仓库识别] 所持道具页面扫描超过 {MaxPages} 页，已停止");
            return true;
        }
        catch (MaaStopException)
        {
            LoggerHelper.Info("[仓库识别] 所持道具扫描已停止");
            return false;
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"[仓库识别] 所持道具扫描失败：{e.Message}");
            return false;
        }
    }

    private static List<VisibleItem> ReadVisibleItems<T>(T context, int[][] nameRois, int[][] countRois, IReadOnlyCollection<string> savedItemNames)
        where T : IMaaContext
    {
        using var image = context.GetImage();
        if (image == null)
            throw new InvalidOperationException("无法获取所持道具页面截图");

        var items = new List<VisibleItem>();
        for (var i = 0; i < Math.Min(4, nameRois.Length); i++)
        {
            var rawName = context.GetText(nameRois[i][0], nameRois[i][1], nameRois[i][2], nameRois[i][3], image)
                .Replace(" ", string.Empty, StringComparison.Ordinal)
                .Trim();
            if (string.IsNullOrWhiteSpace(rawName))
                continue;

            var name = WarehouseScanDraftService.ResolveOtherItemName(rawName, savedItemNames);
            if (!string.Equals(rawName, name, StringComparison.Ordinal))
                LoggerHelper.Info($"[仓库识别] 物品名称纠正：{rawName} → {name}");

            var countText = context.GetText(countRois[i][0], countRois[i][1], countRois[i][2], countRois[i][3], image);
            LoggerHelper.Info($"[仓库识别] {name} 数量 OCR 原文：{countText}");
            if (WarehouseScanDraftService.TryParseCount(countText, out var count))
                items.Add(new VisibleItem(name, count));
        }

        return items;
    }

    private static void ReadKoban<T>(T context, int[] roi, string draftPath) where T : IMaaContext
    {
        using var image = context.GetImage();
        if (image == null)
            throw new InvalidOperationException("无法获取小判页面截图");

        var text = context.GetText(roi[0], roi[1], roi[2], roi[3], image);
        LoggerHelper.Info($"[仓库识别] 小判 OCR 原文：{text}");
        if (!WarehouseScanDraftService.TryParseCount(text, out var value))
        {
            LoggerHelper.Warning("[仓库识别] 小判 OCR 数值无效，保留已有草稿");
            return;
        }

        WarehouseScanDraftService.UpdateCoreResource(draftPath, "小判", value);
        LoggerHelper.Info($"[仓库识别] 小判识别到：{value}");
    }

    private static bool IsAtBottom<T>(T context) where T : IMaaContext
    {
        using var image = context.GetImage();
        if (image == null)
            return false;

        var roi = ScrollbarEndRoi;
        var matched = context.ColorMatch(
            121, 120, 119,
            114, 113, 113,
            image,
            out _,
            threshold: 1.0,
            x: roi[0], y: roi[1], w: roi[2], h: roi[3], count: roi[2] * roi[3]);
        LoggerHelper.Info($"[仓库识别] 所持道具滚动条底部判断：{(matched ? "已到底" : "未到底")}");
        return matched;
    }

    private static void CompleteScan(string draftPath, IReadOnlyList<string> recognizedOrder)
    {
        var completedDraft = WarehouseScanDraftService.Load(draftPath);
        var normalizedItems = WarehouseScanDraftService.NormalizeOtherItems(completedDraft.OtherItems);
        var orderedItems = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var name in recognizedOrder)
        {
            var normalizedName = WarehouseScanDraftService.NormalizeOtherItemName(name);
            if (normalizedItems.TryGetValue(normalizedName, out var count))
                orderedItems[normalizedName] = count;
        }

        foreach (var pair in normalizedItems)
        {
            if (!orderedItems.ContainsKey(pair.Key))
                orderedItems[pair.Key] = pair.Value;
        }

        completedDraft.OtherItems = orderedItems;
        WarehouseScanDraftService.Save(draftPath, completedDraft);
        WarehouseScanDraftService.AppendSnapshot(draftPath, completedDraft.CoreResources);
        LoggerHelper.Info($"[仓库识别] 所持道具扫描完成，共识别 {orderedItems.Count} 种物品，已追加核心资源历史快照");
    }

    private static int[][] ParseRois(JArray? value, int[][] fallback)
    {
        if (value == null)
            return fallback;

        var rois = value.ToObject<int[][]>();
        if (rois == null || rois.Length != fallback.Length || rois.Any(roi => roi.Length != 4))
            throw new InvalidOperationException("所持道具 OCR ROI 必须包含六组 [x, y, w, h]");
        return rois;
    }

    private static int[] ParseRoi(JArray? value, int[] fallback)
    {
        if (value == null)
            return fallback;
        var roi = value.ToObject<int[]>();
        if (roi == null || roi.Length != 4)
            throw new InvalidOperationException("仓库 OCR ROI 必须是 [x, y, w, h]");
        return roi;
    }

    private readonly record struct VisibleItem(string Name, int Count);
}
