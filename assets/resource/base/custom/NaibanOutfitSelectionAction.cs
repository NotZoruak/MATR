using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Extensions.MaaFW.Custom;
using MFAAvalonia.Helper;
using MFAAvalonia.Services;
using System;
using System.IO;
using System.Linq;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>在移除既有安排后，从已保存刀帐读取本轮应优先安排的内番服目标。</summary>
public class NaibanOutfitSelectionAction : IMaaCustomAction
{
    public string Name { get; set; } = nameof(NaibanOutfitSelectionAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            var catalogPath = Path.Combine(AppPaths.ResourceDirectory, "base", "SwordBookCatalog.json");
            var targets = SwordBookNaibanOutfitService.SelectMissingOutfitTargets(catalogPath);
            NaibanOutfitSelectionContext.SetTargets(targets);
            LoggerHelper.Info(targets.Count == 0
                ? "[后勤] 没有待刷取的内番服，将使用任意可用刀剑安排内番"
                : $"[后勤] 待刷取内番服 {string.Join("、", targets.Select(target => target.Name))}");
            return true;
        }
        catch (MaaStopException)
        {
            LoggerHelper.Info("[后勤] 自动内番选刀已停止");
            return false;
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"[后勤] 读取自动内番选刀目标失败: {e.Message}");
            return false;
        }
    }
}
