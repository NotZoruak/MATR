using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Helper;
using System;
using System.Threading;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>
/// 长期远征计划——刷花换队长页面滚动：上滑翻页后在终点保持按压 1s。
/// 通过 touch_down → swipe → sleep → touch_up 序列实现。
/// </summary>
public class PageScrollAndHoldAction : IMaaCustomAction
{
    public string Name { get; set; } = nameof(PageScrollAndHoldAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            ActionParamHelper.ThrowIfStopping(context);
            var tasker = context.Tasker;

            // swipe 滚动页面，500ms
            tasker.Swipe(874, 664, 874, 168, 500);
            ActionParamHelper.ResolveOwnerProcessor(context)?.RecordCustomActionActivity();
            // 在终点按住保持 1s
            tasker.TouchDown(0, 874, 168, 1);
            ActionParamHelper.ResolveOwnerProcessor(context)?.RecordCustomActionActivity();
            Thread.Sleep(1000);
            tasker.TouchUp(0);
            ActionParamHelper.ResolveOwnerProcessor(context)?.RecordCustomActionActivity();

            return true;
        }
        catch (MaaStopException)
        {
            LoggerHelper.Info("[PageScrollAndHold] 手动停止");
            return false;
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"[PageScrollAndHold] 错误: {e.Message}");
            return false;
        }
    }
}
