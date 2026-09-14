using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Extensions;
using MFAAvalonia.Helper;
using MFAAvalonia.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>
/// 选刀列表 OCR 扫描（诊断用）：逐屏识别刀剑列表并向下滑动，直到滑到列表底部，全程不选择任何刀剑。
/// 使用前提：游戏已手动停在编队的「选择刀剑男士」列表页面。
/// 每行输出识别原文、置信度、位置与按刀帐归一的结果，结束时输出汇总与未匹配清单，
/// 并在 debug/sword_list_ocr 下保存一份完整结果，用于整体评估刀名 OCR 的识别情况。
/// </summary>
public class SwordListOcrSweepAction : IMaaCustomAction
{
    private const int DefaultMaxPages = 60;
    private const string UnmatchedResolution = "未匹配";
    private const string TypeLabelResolution = "刀种标签";
    // 上滑后到下一次截图之间的最小等待：L 形收尾已消除惯性，这里只用于避开滑动前的旧帧
    private const int ScrollSettleMilliseconds = ListOcrScan.ScrollSettleMilliseconds;

    /// <summary>刀种名称，用于把列表里的刀种标签行与真正的刀名区分开。</summary>
    private static readonly string[] SwordTypes =
    [
        "大太刀", "短刀", "胁差", "打刀", "太刀", "薙刀", "枪", "剑"
    ];

    public string Name { get; set; } = nameof(SwordListOcrSweepAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        var pages = new List<SwordListOcrSweepPage>();
        var shouldDump = true;

        try
        {
            var json = ActionParamHelper.Parse(args.ActionParam);
            var roi = ParseArray(json["roi"] as JArray) ?? ListOcrScan.SwordListRoi;
            var scroll = ParseArray(json["scroll"] as JArray) ?? ListOcrScan.SwordScroll;
            var maxPages = Math.Max(1, (int?)json["max_pages"] ?? DefaultMaxPages);
            shouldDump = json["dump"]?.Value<bool>() != false;

            var map = FormationContext.SwordTypeMap;
            if (map == null || map.Count == 0)
                map = FormationContext.SwordTypeMap = FormationContext.LoadSwordTypeMap();

            LoggerHelper.Info($"[剑名扫描] 开始扫描：ROI=[{string.Join(",", roi)}] 上滑=[{string.Join(",", scroll)}] 最多 {maxPages} 屏");
            var previousSignature = string.Empty;

            for (var page = 1; page <= maxPages; page++)
            {
                ActionParamHelper.ThrowIfStopping(context);

                using var image = context.GetImage();
                if (image == null)
                {
                    LoggerHelper.Warning("[剑名扫描] 获取截图失败，扫描中止");
                    break;
                }

                var detected = (ListOcrScan.OcrAll(context, image, roi)?.All ?? [])
                    .Where(item => !string.IsNullOrWhiteSpace(item.Text))
                    .OrderBy(item => item.Box is { Count: >= 2 } ? item.Box![1] : 0)
                    .ToList();

                LoggerHelper.Info($"[剑名扫描] 第 {page} 屏，共 {detected.Count} 行");
                var rows = new List<SwordListOcrSweepRow>(detected.Count);
                foreach (var item in detected)
                {
                    var text = item.Text!.Trim();
                    var resolution = ResolveName(text, map);
                    var box = item.Box is { Count: >= 4 } ? item.Box!.ToList() : new List<int>();
                    // 识别结果的分数是可空值，缺失时按 0 处理，避免构造记录时类型不匹配
                    var score = item.Score ?? 0;
                    rows.Add(new SwordListOcrSweepRow(text, score, box, resolution));
                    LoggerHelper.Info($"[剑名扫描] {text} | {score:F4} | [{string.Join(",", box)}] | → {resolution}");
                }

                pages.Add(new SwordListOcrSweepPage(page, rows));

                var signature = string.Join("|", detected
                    .Select(item => item.Text)
                    .OrderBy(text => text, StringComparer.Ordinal));
                if (signature == previousSignature)
                {
                    LoggerHelper.Info($"[剑名扫描] 第 {page} 屏与上一屏相同，判定已滑到列表底部");
                    break;
                }

                previousSignature = signature;
                ListOcrScan.ScrollUp(context, scroll);
                ActionParamHelper.SleepWithStopCheck(context, ScrollSettleMilliseconds);
            }

            Report(pages, shouldDump);
            return true;
        }
        catch (MaaStopException)
        {
            LoggerHelper.Info("[剑名扫描] 手动停止扫描，输出已收集到的部分结果");
            Report(pages, shouldDump);
            return false;
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"[剑名扫描] 扫描异常：{e.Message}");
            Report(pages, shouldDump);
            return false;
        }
    }

    /// <summary>
    /// 把一行识别文本归一到刀帐刀名；读成刀种标签的行（枪、刀、薙刀等）单独标注，
    /// 避免把列表里的刀种图标文字混进未匹配清单。
    /// </summary>
    private static string ResolveName(string text, IReadOnlyDictionary<string, string> swordTypeMap)
    {
        if (SwordNameResolver.TryResolve(text, swordTypeMap, out var swordName)
            && swordTypeMap.TryGetValue(swordName, out var swordType))
            return $"{swordName}（{swordType}）";

        var compact = text.Replace(" ", string.Empty);
        return compact.Length is > 0 and <= 3 && SwordTypes.Any(type => type.Contains(compact, StringComparison.Ordinal))
            ? TypeLabelResolution
            : UnmatchedResolution;
    }

    /// <summary>输出扫描汇总，并把完整结果落盘到 debug/sword_list_ocr。</summary>
    private static void Report(IReadOnlyList<SwordListOcrSweepPage> pages, bool shouldDump)
    {
        var rows = pages.SelectMany(page => page.Rows).ToList();
        if (rows.Count == 0)
        {
            LoggerHelper.Warning("[剑名扫描] 未收集到任何识别结果");
            return;
        }

        var matched = rows
            .Where(row => row.Resolution != UnmatchedResolution && row.Resolution != TypeLabelResolution)
            .ToList();
        var typeLabels = rows.Where(row => row.Resolution == TypeLabelResolution).ToList();
        var unmatched = rows.Where(row => row.Resolution == UnmatchedResolution).ToList();
        var matchedNames = matched.Select(row => row.Text).Distinct(StringComparer.Ordinal).ToList();
        var unmatchedTexts = unmatched.Select(row => row.Text).Distinct(StringComparer.Ordinal).ToList();

        LoggerHelper.Info($"[剑名扫描] 扫描结束：共 {pages.Count} 屏，累计 {rows.Count} 行，归一出的刀名 {matchedNames.Count} 个");
        LoggerHelper.Info($"[剑名扫描] 已归一 {matched.Count} 行，刀种标签 {typeLabels.Count} 行，未匹配 {unmatched.Count} 行");
        if (unmatchedTexts.Count > 0)
            LoggerHelper.Info($"[剑名扫描] 未匹配清单：{string.Join("、", unmatchedTexts)}");

        if (!shouldDump)
            return;

        try
        {
            var directory = Path.Combine(AppPaths.InstallRoot, "debug", "sword_list_ocr");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"sweep_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            var content = new
            {
                scannedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                pageCount = pages.Count,
                rowCount = rows.Count,
                matchedCount = matched.Count,
                typeLabelCount = typeLabels.Count,
                unmatchedCount = unmatched.Count,
                unmatchedTexts,
                rows = pages.SelectMany(page => page.Rows.Select(row => new
                {
                    page = page.Page,
                    text = row.Text,
                    score = Math.Round(row.Score, 4),
                    box = row.Box,
                    resolution = row.Resolution,
                })),
            };
            File.WriteAllText(path, JsonConvert.SerializeObject(content, Formatting.Indented));
            LoggerHelper.Info($"[剑名扫描] 结果已保存：{path}");
        }
        catch (Exception e)
        {
            LoggerHelper.Warning($"[剑名扫描] 保存扫描结果失败：{e.Message}");
        }
    }

    /// <summary>解析 [x, y, w, h] 参数，缺省或格式不符时返回 null，由调用方取默认值。</summary>
    private static int[]? ParseArray(JArray? value) => value is { Count: 4 } ? value.ToObject<int[]>() : null;
}

/// <summary>一屏的扫描结果。</summary>
public sealed record SwordListOcrSweepPage(int Page, List<SwordListOcrSweepRow> Rows);

/// <summary>一行识别结果及其归一结论。</summary>
public sealed record SwordListOcrSweepRow(string Text, double Score, List<int> Box, string Resolution);
