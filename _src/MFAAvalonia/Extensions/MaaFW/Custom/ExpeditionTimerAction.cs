using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using System;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>
/// 后勤后台计时器动作：记录倒计时起点，供 ExpeditionTimerRecognition 检查。
/// 当全局开关"远征智能调度"开启时，自动 OCR 远征和已启用内番的最早完成时间。
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
            int configuredInterval = (int?)json["interval"] ?? 600;
            int intervalSeconds = configuredInterval;

            // 智能调度：OCR 后勤事项剩余时间，动态调整计时器间隔
            if (ExpeditionTimeTracker.IsSmartSchedulingEnabled())
            {
                try
                {
                    var earliest = ExpeditionTimeTracker.ScanAndStore(context);
                    if (earliest.HasValue && earliest.Value >= 0)
                    {
                        intervalSeconds = Math.Min(earliest.Value, configuredInterval);
                        LoggerHelper.Info($"[后勤计时] 最早目标完成 {earliest.Value}s, 实际等待 {intervalSeconds}s");
                    }
                }
                catch (Exception ex)
                {
                    LoggerHelper.Warning($"[后勤计时] 智能 OCR 失败，回退固定间隔 {configuredInterval}秒: {ex.Message}");
                }
                // 始终关闭队伍状态面板（E_AllTeamsBusy 被改为 DoNothing，不点的话面板不会关）
                try { context.Click(ExpeditionTimeTracker.ClosePanelX, ExpeditionTimeTracker.ClosePanelY); }
                catch (Exception ex) { LoggerHelper.Warning($"[后勤计时] 关闭面板失败: {ex.Message}"); }

                ExpeditionTimerRecognition.StartTimer(intervalSeconds);
                var display = intervalSeconds >= 60
                    ? $"{intervalSeconds / 60}分{intervalSeconds % 60}s"
                    : $"{intervalSeconds}s";
                var fileMessage = $"[后勤计时] 倒计时开始：{display}";
                var guiMessage = $"[后勤计时] {display}";
                // 文件日志保留词表格式，供工作记录解析器识别。
                LoggerHelper.Info(fileMessage);
                try { ActionParamHelper.ResolveOwnerProcessor(context)?.AddLog(guiMessage); } catch { }
            }
            else
            {
                // 智能调度关闭：只关面板，不启计时器
                try { context.Click(ExpeditionTimeTracker.ClosePanelX, ExpeditionTimeTracker.ClosePanelY); }
                catch (Exception ex) { LoggerHelper.Warning($"[后勤计时] 关闭面板失败: {ex.Message}"); }
            }

            return true;
        }
        catch (MaaStopException)
        {
            return false;
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"[后勤计时] 错误: {e.Message}");
            return false;
        }
    }
}
