using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using System;
using System.Linq;
using System.Threading;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>
/// 长期远征计划——疲劳值检测。
/// 两套 ROI：远征队伍面板（check_all）和出阵编队页面（check_captain）。
/// 游戏中的"疲劳值"实际是心情值，越高越好。OCR 结果格式为 XX/100。
/// </summary>
public class FatigueCheckAction : IMaaCustomAction
{
    public string Name { get; set; } = nameof(FatigueCheckAction);

    /// <summary>远征队伍面板——六个疲劳 OCR ROI</summary>
    public static readonly int[][] FatigueRoisExpedition = FatigueRecognitionHelper.FatigueRoisExpedition;

    /// <summary>出阵编队页面——六个疲劳 OCR ROI（复用 DragCaptainAction）</summary>
    public static readonly int[][] FatigueRoisSortie = FatigueRecognitionHelper.FatigueRoisSortie;

    /// <summary>OCR 六个位置的疲劳值，空槽位或失败返回 null。返回 [0..5] 对应位置一~六</summary>
    public static int?[] ReadFatigue<T>(T context, int[][] rois) where T : IMaaContext
        => FatigueRecognitionHelper.ReadFatigue(context, rois);

    /// <summary>找最低疲劳值的索引和值。无可用位置返回 (-1, -1)</summary>
    public static (int Index, int Value) FindLowest(int?[] values)
        => FatigueRecognitionHelper.FindLowest(values);

    /// <summary>获取用户阈值，默认 91。「疲劳阈值」是「长期远征计划」的子选项，需穿透 SubOptions 查找。</summary>
    public static int GetThreshold() => FatigueRecognitionHelper.GetThreshold();

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            ActionParamHelper.ThrowIfStopping(context);
            var json = ActionParamHelper.Parse(args.ActionParam);
            var mode = (string?)json["mode"] ?? "check_all";
            var threshold = (int?)json["threshold"] ?? GetThreshold();

            // check_first 模式：仅 OCR 出阵编队页面首位疲劳值，不走通用六位扫描
            if (mode == "check_first")
            {
                var sortieRoi = FatigueRoisSortie[0];
                int? firstValue = null;
                using (var image = context.GetImage())
                {
                    if (image != null)
                    {
                        firstValue = FatigueRecognitionHelper.ParseFatigueValue(
                            FatigueRecognitionHelper.ReadFatigueText(context, sortieRoi, image));
                    }
                }
                for (int retry = 0; retry < 10 && !firstValue.HasValue; retry++)
                {
                    LoggerHelper.Info($"[疲劳检测-出阵] 首位 OCR 失败，重试 {retry + 1}/10");
                    Thread.Sleep(200);
                    using var retryImage = context.GetImage();
                    if (retryImage == null) continue;
                    firstValue = FatigueRecognitionHelper.ParseFatigueValue(
                        FatigueRecognitionHelper.ReadFatigueText(context, sortieRoi, retryImage));
                }
                if (!firstValue.HasValue)
                {
                    LoggerHelper.Warning("[疲劳检测-出阵] 首位疲劳值识别失败，继续出阵");
                    return FatigueCheckDecision.ShouldContinueWhenFirstValueUnreadable(firstValue);
                }
                var reversed = (bool?)json["reversed"] ?? false;
                var ok = reversed ? firstValue.Value < threshold : firstValue.Value >= threshold;
                LoggerHelper.Info($"[疲劳检测-出阵] 首位={firstValue}, 阈值={threshold}, reversed={reversed}, 结果={ok}");
                FlowerStateTracker.CurrentFatigueLowest = firstValue.Value;
                if (!ok)
                {
                    var msg = reversed
                        ? $"[出阵疲劳检测] 检测到首位疲劳已恢复到{threshold}，刷花结束"
                        : $"[出阵疲劳检测] 检测到首位疲劳低于{threshold}，进入刷花";
                    try { ActionParamHelper.ResolveOwnerProcessor(context)?.AddLog(msg); } catch { }
                }
                return ok;
            }

            var rois = mode == "check_captain" ? FatigueRoisSortie : FatigueRoisExpedition;
            var values = ReadFatigue(context, rois);

            // check_all 模式下首位必须读到疲劳值，OCR 失败时重试（最多 10 次，每次间隔 200ms）
            if (mode != "check_captain")
            {
                for (int retry = 0; retry < 10 && !values[0].HasValue; retry++)
                {
                    LoggerHelper.Info($"[疲劳检测] 首位 OCR 失败，重试 {retry + 1}/10");
                    Thread.Sleep(200);
                    using var retryImage = context.GetImage();
                    if (retryImage == null) continue;
                    values[0] = FatigueRecognitionHelper.ParseFatigueValue(
                        FatigueRecognitionHelper.ReadFatigueText(context, rois[0], retryImage));
                }
            }

            var (bestPos, bestVal) = FindLowest(values);

            LoggerHelper.Info($"[疲劳检测] mode={mode}, 阈值={threshold}");
            LoggerHelper.Info($"[疲劳检测] 六位疲劳: [{string.Join(", ", values.Select(v => v?.ToString() ?? "空"))}]");

            var team = (int?)json["team"] ?? 0;

            if (mode == "check_captain")
            {
                if (!values[0].HasValue) { LoggerHelper.Warning("[疲劳检测] 队长位 OCR 失败"); return false; }
                var ok = values[0].Value >= threshold;
                LoggerHelper.Info($"[疲劳检测] 首位={values[0]}, >= {threshold}? {ok}");
                FlowerStateTracker.CurrentFatigueLowest = values[0].Value;
                if (ok)
                    try { ActionParamHelper.ResolveOwnerProcessor(context)?.AddLog($"[远征疲劳检测] 疲劳值恢复完成"); } catch { }
                return ok;
            }
            else
            {
                if (bestPos < 0) { LoggerHelper.Info("[疲劳检测] 全空槽位，视为合格"); return true; }
                var ok = bestVal >= threshold;
                LoggerHelper.Info($"[疲劳检测] 最低位=位置{bestPos + 1}, 值={bestVal}, >= {threshold}? {ok}");
                FlowerStateTracker.CurrentFatigueLowest = bestVal;
                if (!ok)
                {
                    if (team > 0) FlowerStateTracker.BeginTeam(team);
                    try { ActionParamHelper.ResolveOwnerProcessor(context)?.AddLog($"[远征疲劳检测] 有刀剑疲劳低于阈值，进入刷花"); } catch { }
                }
                return ok;
            }
        }
        catch (MaaStopException) { LoggerHelper.Info("[疲劳检测] 手动停止"); return false; }
        catch (Exception e) { LoggerHelper.Error($"[疲劳检测] 错误: {e.Message}"); return false; }
    }
}
