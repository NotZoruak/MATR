using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>
/// 后勤完成时间扫描：OCR 五个部队的远征剩余时间，并按设置读取内番剩余时间，
/// 计算最早完成时刻存入 ExpeditionReturnTracker。
/// </summary>
public class ExpeditionTimeTracker : IMaaCustomAction
{
    public string Name { get; set; } = nameof(ExpeditionTimeTracker);

    /// <summary>五个部队剩余时间的 OCR ROI（可经由 action_param 覆盖）</summary>
    /// 与 E_CheckTeam1~5 的 OCR ROI 保持一致
    public static readonly int[][] DefaultRois =
    [
        [166, 38, 159, 31],   // 部队一
        [166, 152, 159, 31],  // 部队二
        [166, 268, 159, 31],  // 部队三
        [166, 383, 159, 31],  // 部队四
        [166, 498, 159, 31],  // 部队五
    ];

    /// <summary>关闭面板的点击坐标（target [230,12,71,19] + offset [6,4]）</summary>
    public const int ClosePanelX = 236;
    public const int ClosePanelY = 16;

    /// <summary>今日内番表剩余时间 OCR 区域</summary>
    public static readonly int[] NaibanRemainingTimeRoi = [1050, 310, 88, 24];

    /// <summary>
    /// 扫描队伍状态面板，计算最早远征或内番完成剩余秒数并存入 ExpeditionReturnTracker。
    /// 供 ExpeditionTimerAction 在智能调度时复用。
    /// </summary>
    /// <returns>最早远征归队或内番完成剩余秒数，无进行中目标时返回 null</returns>
    public static int? ScanAndStore<T>(T context) where T : IMaaContext
    {
        var tasks = ExpeditionOptionResolver.GetCurrentTasks(context);
        var expeditionEnabled = ExpeditionOptionResolver.IsExpeditionEnabled(tasks);
        var naibanEnabled = ExpeditionOptionResolver.IsNaibanEnabled(tasks);
        if (!expeditionEnabled && !naibanEnabled)
        {
            ExpeditionReturnTracker.Reset();
            LoggerHelper.Info("[后勤计时] 远征和内番均未开启，按刷新间隔计时");
            return null;
        }

        var expeditionRemainingSeconds = new List<int?>(expeditionEnabled ? 5 : 0);

        using var image = context.GetImage();
        if (expeditionEnabled)
        {
            for (int i = 0; i < 5; i++)
            {
                var roi = DefaultRois[i];
                var text = image != null
                    ? context.GetText(roi[0], roi[1], roi[2], roi[3], image)
                    : null;
                LoggerHelper.Info($"[后勤计时] 部队{i + 1} OCR: '{text}'");

                expeditionRemainingSeconds.Add(ParseRemainingSeconds(text));
            }
        }
        else
        {
            LoggerHelper.Info("[后勤计时] 远征未开启，跳过部队剩余时间扫描");
        }

        int? naibanRemainingSeconds = null;
        if (naibanEnabled && image != null)
        {
            var roi = NaibanRemainingTimeRoi;
            var text = context.GetText(roi[0], roi[1], roi[2], roi[3], image);
            LoggerHelper.Info($"[后勤计时] 内番 OCR: '{text}'");
            naibanRemainingSeconds = ParseRemainingSeconds(text);
        }

        var minRemaining = GetEarliestRemainingSeconds(
            expeditionRemainingSeconds,
            naibanRemainingSeconds,
            expeditionEnabled,
            naibanEnabled);

        if (minRemaining.HasValue)
        {
            var completionTime = DateTime.Now.AddSeconds(minRemaining.Value);
            ExpeditionReturnTracker.SetEarliestReturn(completionTime);
            LoggerHelper.Info($"[后勤计时] 最早完成: {completionTime:HH:mm:ss}（{minRemaining.Value}秒）");
        }
        else
        {
            ExpeditionReturnTracker.Reset();
            LoggerHelper.Info("[后勤计时] 无进行中的远征或内番，重置追踪器");
        }

        return minRemaining;
    }

    /// <summary>从远征和可选的内番剩余时间中选择最早完成时间。</summary>
    public static int? GetEarliestRemainingSeconds(
        IEnumerable<int?> expeditionRemainingSeconds,
        int? naibanRemainingSeconds,
        bool includeExpedition,
        bool includeNaiban)
    {
        int? earliest = null;
        if (includeExpedition)
        {
            foreach (var seconds in expeditionRemainingSeconds)
            {
                if (seconds.HasValue && seconds.Value >= 0 && (!earliest.HasValue || seconds.Value < earliest.Value))
                    earliest = seconds.Value;
            }
        }

        if (includeNaiban
            && naibanRemainingSeconds.HasValue
            && naibanRemainingSeconds.Value >= 0
            && (!earliest.HasValue || naibanRemainingSeconds.Value < earliest.Value))
        {
            earliest = naibanRemainingSeconds.Value;
        }

        return earliest;
    }

    /// <summary>检查全局开关"远征智能调度"是否开启</summary>
    public static bool IsSmartSchedulingEnabled()
    {
        try
        {
            var iface = MaaProcessor.Interface;
            var globalOpts = iface?.GlobalSelectOptions;
            var smartOpt = globalOpts?.FirstOrDefault(o => o.Name == "远征智能调度");
            // switch 类型通过 Index 判断（0=Yes/开启, 非0=No/关闭），不能用 SelectedCases
            return smartOpt?.Index == 0;
        }
        catch { return false; }
    }

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            ActionParamHelper.ThrowIfStopping(context);
            ScanAndStore(context);

            // 点击关闭队伍状态面板
            context.Click(ClosePanelX, ClosePanelY);
            LoggerHelper.Info("[后勤计时] 已点击关闭面板");

            return true;
        }
        catch (MaaStopException)
        {
            LoggerHelper.Info("[后勤计时] 检测到手动停止");
            return false;
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"[后勤计时] 错误: {e.Message}");
            return false;
        }
    }

    /// <summary>解析剩余时间文本，忽略待机状态并容忍括号缺失及全角冒号。</summary>
    public static int? ParseRemainingSeconds(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var clean = text.Trim();

        // 待机/空闲 = 无远征
        if (clean.Contains("待机") || clean.Contains("空闲"))
            return null;

        var match = Regex.Match(clean, @"\d{1,3}[:：]\d{1,2}(?:[:：]\d{1,2})?");
        if (!match.Success)
            return null;

        var parts = match.Value.Replace('：', ':').Split(':');
        if (parts.Length == 3 &&
            int.TryParse(parts[0], out var h) &&
            int.TryParse(parts[1], out var m) &&
            int.TryParse(parts[2], out var s))
        {
            return h * 3600 + m * 60 + s;
        }

        // 尝试 MM:SS
        if (parts.Length == 2 &&
            int.TryParse(parts[0], out var m2) &&
            int.TryParse(parts[1], out var s2))
        {
            return m2 * 60 + s2;
        }

        return null;
    }
}
