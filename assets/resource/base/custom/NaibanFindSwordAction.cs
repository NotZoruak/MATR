using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>自动内番选刀：优先查找已上锁且可选的目标，找不到或无目标时选择当前列表中第一把可选刀剑。</summary>
public class NaibanFindSwordAction : IMaaCustomAction
{
    private static readonly int[] SwordListRoi = [168, 126, 165, 566];
    /// <summary>内番选刀列表上滑坐标：与编队选刀保持一致，起点右移到状态列上方</summary>
    private static readonly int[] SwordScroll = [306, 624, 306, 128];

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
                && ListOcrScan.ScanAndClick(context, target.Name, SwordListRoi, SwordScroll,
                    (image, box) => ClickSword(context, box[1]), "NaibanFindSword",
                    hitFilter: (image, box) => IsLockedAndSelectable(context, image, box[1]),
                    minScore: ListOcrScan.SwordNameMinScore))
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

        var hit = ListOcrScan.OcrAll(context, image, SwordListRoi)?.All
            .Where(result => result.Score >= ListOcrScan.SwordNameMinScore && result.Box is { Count: >= 4 })
            .Where(result => IsSelectable(context, image, result.Box![1]))
            .OrderBy(result => result.Box![1])
            .FirstOrDefault();
        if (hit?.Box is not { Count: >= 4 })
            return false;

        return ClickSword(context, hit.Box[1]);
    }

    /// <summary>使用刀名识别的同一帧与行坐标，检查金色锁标记并排除灰色的不可选按钮。</summary>
    private static bool IsLockedAndSelectable<T>(T context, IMaaImageBuffer image, int y) where T : IMaaContext
        => context.ColorMatch(212, 173, 31, 212, 173, 31, image, out _,
               threshold: 1.0, x: 87, y: y, w: 1, h: 1, count: 1)
           && IsSelectable(context, image, y);

    /// <summary>排除灰色的不可选按钮；补位时不要求刀剑已上锁。</summary>
    private static bool IsSelectable<T>(T context, IMaaImageBuffer image, int y) where T : IMaaContext
        => !context.ColorMatch(190, 190, 190, 190, 190, 190, image, out _,
               threshold: 1.0, x: 1202, y: y, w: 1, h: 1, count: 1);

    private static bool ClickSword<T>(T context, int y) where T : IMaaContext
    {
        context.ClickWithProgress(1214, y);
        return true;
    }
}
