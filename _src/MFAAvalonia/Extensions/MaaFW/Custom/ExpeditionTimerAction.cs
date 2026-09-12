using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using System;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>
/// 远征后台计时器动作：记录倒计时起点，供 ExpeditionTimerRecognition 检查。
/// 当全局开关"远征智能调度"开启时，自动 OCR 部队面板计算最早归队时间。
/// </summary>
public class ExpeditionTimerAction : IMaaCustomAction
{
    public string Name { get; set; } = nameof(ExpeditionTimerAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            ActionParamHelper.ThrowIfStopping(context);

            var json = ActionParamHelper.Parse(args.ActionParam);
            var mode = (string?)json["mode"] ?? "start";

            if (mode == "reset")
            {
                ExpeditionTimerRecognition.ResetTimer();
                return true;
            }

            int configuredInterval = (int?)json["interval"] ?? 600;
            int intervalSeconds = configuredInterval;

            // 智能调度：OCR 部队面板剩余时间，动态调整计时器间隔
            if (ExpeditionTimeTracker.IsSmartSchedulingEnabled())
            {
                try
                {
                    var earliest = ExpeditionTimeTracker.ScanAndStore(context);
                    if (earliest.HasValue && earliest.Value > 0)
                    {
                        intervalSeconds = Math.Min(earliest.Value, configuredInterval);
                        LoggerHelper.Info($"[远征计时] 最早 {earliest.Value}s, 实际 {intervalSeconds}s");
                    }
                }
                catch (Exception ex)
                {
                    LoggerHelper.Warning($"[远征计时] 智能 OCR 失败，回退固定间隔 {configuredInterval}秒: {ex.Message}");
                }
                // 始终关闭队伍状态面板（E_AllTeamsBusy 被改为 DoNothing，不点的话面板不会关）
                try { context.Click(ExpeditionTimeTracker.ClosePanelX, ExpeditionTimeTracker.ClosePanelY); }
                catch (Exception ex) { LoggerHelper.Warning($"[远征计时] 关闭面板失败: {ex.Message}"); }

                ExpeditionTimerRecognition.StartTimer(intervalSeconds);
                var display = intervalSeconds >= 60
                    ? $"{intervalSeconds / 60}分{intervalSeconds % 60}s"
                    : $"{intervalSeconds}s";
                var fileMessage = $"[远征计时] 倒计时开始：{display}";
                var guiMessage = $"[远征计时] {display}";
                // 文件日志保留词表格式，供工作记录解析器识别。
                LoggerHelper.Info(fileMessage);
                try { ActionParamHelper.ResolveOwnerProcessor(context)?.AddLog(guiMessage); } catch { }
            }
            else
            {
                // 智能调度关闭：只关面板，不启计时器
                try { context.Click(ExpeditionTimeTracker.ClosePanelX, ExpeditionTimeTracker.ClosePanelY); }
                catch (Exception ex) { LoggerHelper.Warning($"[远征计时] 关闭面板失败: {ex.Message}"); }
            }

            return true;
        }
        catch (MaaStopException)
        {
            return false;
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"[远征计时] 错误: {e.Message}");
            return false;
        }
    }
}
