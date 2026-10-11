using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.Pages;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>扫描刀帐当前页的序号和四项立绘拥有状态。</summary>
public sealed class SwordBookScanAction : IMaaCustomAction
{
    private static readonly int[] NumberRoi = [548, 5, 184, 40];
    private static readonly int[] NextPage = [764, 2, 34, 44];
    private static readonly int[] WoundedRoi = [921, 669, 6, 6];
    private static readonly int[] TrueSwordRoi = [986, 668, 6, 7];
    private static readonly int[] InnerCareRoi = [1049, 667, 6, 7];
    private static readonly int[] CasualRoi = [1112, 667, 6, 7];
    private const int MaxPages = 300;
    private const int NumberFreezeMilliseconds = 300;
    private const int MaxNumberWaits = 20;
    private const int MaxClickAttempts = 4;
    private const int RequiredStableReads = 3;

    public string Name { get; set; } = nameof(SwordBookScanAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            return RunCore(context);
        }
        catch (MaaStopException)
        {
            LoggerHelper.Info("[刀帐] 检测到 MaaFW 停止请求，已结束本轮扫描");
            return false;
        }
    }

    private static bool RunCore<T>(T context) where T : IMaaContext
    {
        var draft = LoadDraft();
        var startNumber = ReadStartNumber(context);
        if (startNumber == null)
        {
            LoggerHelper.Warning("[刀帐] 自动识别失败：顶部未同时识别到“序号”和刀剑数字，请先切换到具体刀剑男士的刀帐页面");
            return false;
        }

        LoggerHelper.Info($"[刀帐] 自动识别开始：起始序号={startNumber}");
        var currentNumber = startNumber.Value;

        for (var page = 0; page < MaxPages; page++)
        {
            ActionParamHelper.ThrowIfStopping(context);
            using var image = context.GetImage();
            if (image == null)
                throw new Exception("刀帐自动识别失败：无法获取游戏画面");

            var number = currentNumber.ToString();
            var state = new SwordBookPortraitState(
                number,
                true,
                IsOwned(context, image, WoundedRoi),
                IsOwned(context, image, TrueSwordRoi),
                IsOwned(context, image, InnerCareRoi),
                IsOwned(context, image, CasualRoi));
            draft[currentNumber.ToString()] = state;
            SaveDraft(draft);
            LoggerHelper.Info($"[刀帐] 序号={currentNumber} 中伤={state.Wounded} 真剑={state.TrueSword} 内番={state.InnerCare} 轻装={state.Casual}");

            var nextNumber = WaitForChangedNumber(context, currentNumber, startNumber.Value);
            if (nextNumber == startNumber)
            {
                SaveDraft(draft);
                LoggerHelper.Info($"[刀帐] 自动识别完成：已回到起始序号={startNumber}");
                return true;
            }

            currentNumber = nextNumber;
        }

        throw new Exception($"刀帐自动识别失败：扫描超过 {MaxPages} 页仍未回到起始序号");
    }

    private static int? ReadStartNumber<T>(T context) where T : IMaaContext
    {
        using var image = context.GetImage();
        if (image == null)
            return null;
        var text = context.GetText(NumberRoi[0], NumberRoi[1], NumberRoi[2], NumberRoi[3], image);
        if (!text.Contains("序号", StringComparison.Ordinal))
            return null;
        return ParseNumber(text);
    }

    private static int? ReadNumber<T>(T context) where T : IMaaContext
    {
        using var image = context.GetImage();
        if (image == null)
            return null;
        var text = context.GetText(NumberRoi[0], NumberRoi[1], NumberRoi[2], NumberRoi[3], image);
        return ParseNumber(text);
    }

    private static int? ParseNumber(string text)
    {
        var number = text.ToInt();
        return number > 0 || text.Contains("0", StringComparison.Ordinal) || text.Contains("〇", StringComparison.Ordinal)
            ? number
            : null;
    }

    private static int WaitForChangedNumber<T>(T context, int previous, int startNumber) where T : IMaaContext
    {
        for (var attempt = 0; attempt < MaxClickAttempts; attempt++)
        {
            ActionParamHelper.ThrowIfStopping(context);
            context.ClickWithProgress(NextPage[0] + NextPage[2] / 2, NextPage[1] + NextPage[3] / 2);
            var number = WaitForNumberChange(context, previous, startNumber);
            if (number.HasValue)
                return number.Value;

            // 当前页面出现异常 OCR 时，下一轮仍从箭头重新尝试，避免点击数字区域造成跳页。
        }

        throw new Exception($"刀帐自动识别失败：点击翻页后序号仍为 {previous}");
    }

    private static int? WaitForNumberChange<T>(T context, int previous, int startNumber) where T : IMaaContext
    {
        var stableNumber = int.MinValue;
        var stableReads = 0;
        for (var i = 0; i < MaxNumberWaits; i++)
        {
            ActionParamHelper.SleepWithStopCheck(context, NumberFreezeMilliseconds);
            var number = ReadNumber(context);
            if (number.HasValue && number.Value != previous && (number.Value > previous || number.Value == startNumber))
            {
                if (number.Value == stableNumber)
                    stableReads++;
                else
                {
                    stableNumber = number.Value;
                    stableReads = 1;
                }

                if (stableReads >= RequiredStableReads)
                    return number.Value;
            }
            else
            {
                stableNumber = int.MinValue;
                stableReads = 0;
            }
        }

        return null;
    }

    private static bool IsOwned<T>(T context, IMaaImageBuffer image, int[] roi) where T : IMaaContext
    {
        return context.ColorMatch(
            189, 189, 189,
            189, 189, 189,
            image,
            out _,
            threshold: 1.0,
            x: roi[0], y: roi[1], w: roi[2], h: roi[3], count: 1);
    }

    private static Dictionary<string, SwordBookPortraitState> LoadDraft()
    {
        var path = DraftPath;
        if (!File.Exists(path))
            return new(StringComparer.Ordinal);

        try
        {
            var entries = JsonConvert.DeserializeObject<List<SwordBookPortraitState>>(File.ReadAllText(path)) ?? [];
            return entries.ToDictionary(entry => entry.Number, StringComparer.Ordinal);
        }
        catch (Exception e)
        {
            LoggerHelper.Warning($"[刀帐] 读取自动识别草稿失败：{e.Message}");
            return new(StringComparer.Ordinal);
        }
    }

    private static void SaveDraft(Dictionary<string, SwordBookPortraitState> draft)
    {
        Directory.CreateDirectory(AppPaths.ConfigDirectory);
        File.WriteAllText(DraftPath, JsonConvert.SerializeObject(draft.Values.OrderBy(entry => int.Parse(entry.Number)).ToList(), Formatting.Indented));
    }

    private static string DraftPath => Path.Combine(AppPaths.ConfigDirectory, "swordbook_scan.json");
}
