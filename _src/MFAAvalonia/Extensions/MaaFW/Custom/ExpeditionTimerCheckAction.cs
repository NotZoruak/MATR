using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>
/// 远征计时器检查动作：未过期返回 true 继续主任务；到期时把本 node 的 on_error 目标改写成 next 后返回 true。
///
/// 计时到期是预期分支，而 MaaFW 的 SaveOnError 会对任何走 on_error 的 node 往 debug/on_error 写一张截图，
/// 每个智能调度周期都会多出一张无意义的 E_CheckTimerExpired 截图，因此这里改走 next。
/// on_error 仍保留为兜底：读不到 node 配置时返回 false，落回原来的错误路径。
/// </summary>
public class ExpeditionTimerCheckAction : IMaaCustomAction
{
    public string Name { get; set; } = nameof(ExpeditionTimerCheckAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            ActionParamHelper.ThrowIfStopping(context);

            if (ExpeditionTimerRecognition.IsExpired())
            {
                // 智能调度关闭时计时器从未启动，不输出误导性日志
                if (ExpeditionTimeTracker.IsSmartSchedulingEnabled())
                {
                    var msg = "[远征计时] 倒计时结束";
                    LoggerHelper.Info(msg);
                    try { ActionParamHelper.ResolveOwnerProcessor(context)?.AddLog(msg); } catch { }
                }

                return RedirectToConfiguredErrorPath(context, args.NodeName);
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

    /// <summary>
    /// 读取本 node 配置的 on_error 目标并改写成 next，使到期分支以成功路径结束。
    /// 读不到目标时返回 false，完整保留原来的 on_error 行为（各任务覆盖里的回本丸链路）。
    /// </summary>
    private static bool RedirectToConfiguredErrorPath<T>(T context, string nodeName)
        where T : IMaaContext
    {
        try
        {
            var targets = ReadConfiguredErrorPath(context, nodeName);
            if (targets.Count == 0)
            {
                LoggerHelper.Warning($"[远征计时] 未读到 {nodeName} 的 on_error 目标，按原错误路径返回本丸");
                return false;
            }

            context.OverrideNext(nodeName, targets);
            return true;
        }
        catch (Exception e)
        {
            LoggerHelper.Warning($"[远征计时] 改走 next 失败，落回 on_error：{e.Message}");
            return false;
        }
    }

    /// <summary>读取 node 的 on_error 列表；取不到时返回空列表。</summary>
    private static System.Collections.Generic.List<string> ReadConfiguredErrorPath<T>(T context, string nodeName)
        where T : IMaaContext
    {
        if (!context.GetNodeData(nodeName, out var nodeData) || string.IsNullOrWhiteSpace(nodeData))
            return [];

        var errorPath = JObject.Parse(nodeData)["on_error"]?
            .Values<string>()
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToList();
        return errorPath ?? [];
    }
}
