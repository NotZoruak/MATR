using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>自动内番选刀：优先精确查找目标刀剑，找不到或无目标时选择当前列表中第一把可用刀剑。</summary>
public class NaibanFindSwordAction : IMaaCustomAction
{
    private static readonly int[] SwordListRoi = [168, 126, 165, 566];
    private static readonly int[] SwordScroll = [106, 624, 106, 128];

    public string Name { get; set; } = nameof(NaibanFindSwordAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            var json = ActionParamHelper.Parse(args.ActionParam);
            int slot = json["slot"]?.Value<int>() ?? 0;
            if (slot is < 1 or > 2)
                return false;

            if (NaibanOutfitSelectionContext.TryGetTarget(slot, out var target)
                && FormationScan.ScanAndClick(context, target.Name, SwordListRoi, SwordScroll,
                    box => ClickSword(context, box[1]), "NaibanFindSword", exactMatch: true))
                return true;

            LoggerHelper.Warning($"[后勤] 第{slot}位未找到优先目标，将选择任意可用刀剑");
            return ClickFirstAvailable(context);
        }
        catch (MaaStopException)
        {
            return false;
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"[后勤] 查找内番刀剑失败: {e.Message}");
            return false;
        }
    }

    private static bool ClickFirstAvailable<T>(T context) where T : IMaaContext
    {
        using var image = context.GetImage();
        if (image == null)
            return false;

        var hit = FormationScan.OcrAll(context, image, SwordListRoi)?.All
            .Where(result => result.Score >= FormationScan.MinScore && result.Box is { Count: >= 4 })
            .OrderBy(result => result.Box![1])
            .FirstOrDefault();
        if (hit?.Box is not { Count: >= 4 })
            return false;

        return ClickSword(context, hit.Box[1]);
    }

    private static bool ClickSword<T>(T context, int y) where T : IMaaContext
    {
        context.Click(1214, y);
        return true;
    }
}
