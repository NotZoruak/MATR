using Avalonia;
using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Helper;
using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>统计演练对手的危险度，达到配置的威胁度阈值时返回失败。</summary>
public class DrillDangerCheckAction : IMaaCustomAction
{
    private static readonly int[][] ExtremeMarkerRois =
    [
        [298, 141, 4, 5],
        [298, 235, 4, 5],
        [298, 329, 4, 5],
        [298, 423, 4, 5],
        [298, 517, 4, 5],
        [298, 611, 4, 5],
    ];
    private static readonly int[] SwordNameRoi = [105, 181, 227, 502];

    public string Name { get; set; } = nameof(DrillDangerCheckAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            var threshold = 6;
            if (!string.IsNullOrWhiteSpace(args.ActionParam))
            {
                var param = ActionParamHelper.Parse(args.ActionParam);
                threshold = Math.Clamp((int?)param["threshold"] ?? 6, 1, 6);
            }

            using var image = context.GetImage();
            if (image == null)
            {
                LoggerHelper.Warning("[日课 演练] 获取对手情报截图失败，按强敌处理");
                return false;
            }

            if (image is not MaaImageBuffer imageBuffer)
            {
                LoggerHelper.Warning("[日课 演练] 截图缓冲区类型不受支持，按强敌处理");
                return false;
            }

            using var bitmap = imageBuffer.ToBitmap();
            if (bitmap == null)
            {
                LoggerHelper.Warning("[日课 演练] 读取对手情报截图失败，按强敌处理");
                return false;
            }

            var danger = ExtremeMarkerRois.Count(roi => IsAllWhite(bitmap, roi));
            var names = ListOcrScan.OcrAll(context, image, SwordNameRoi)?.All ?? [];
            if (names.Any(item => item.Text?.Contains("丙子", StringComparison.Ordinal) == true))
                danger++;

            LoggerHelper.Info($"[日课 演练] 对手危险度={danger}，避战阈值={threshold}");
            return DrillDangerDecision.ShouldEnterTraining(danger, threshold);
        }
        catch (MaaStopException)
        {
            LoggerHelper.Info("[日课 演练] 手动停止强敌判断");
            return false;
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"[日课 演练] 强敌判断异常：{e.Message}");
            return false;
        }
    }

    /// <summary>仅当标志区域全部为白色时，判定该刀剑已经极化。</summary>
    private static bool IsAllWhite(Avalonia.Media.Imaging.Bitmap bitmap, int[] roi)
    {
        var pixelBytes = new byte[roi[2] * roi[3] * 4];
        var handle = GCHandle.Alloc(pixelBytes, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(roi[0], roi[1], roi[2], roi[3]), handle.AddrOfPinnedObject(), pixelBytes.Length, roi[2] * 4);
        }
        finally
        {
            handle.Free();
        }

        for (var index = 0; index < pixelBytes.Length; index += 4)
        {
            var blue = pixelBytes[index];
            var green = pixelBytes[index + 1];
            var red = pixelBytes[index + 2];
            if (red < 254 || green < 254 || blue < 254)
                return false;
        }

        return true;
    }
}
