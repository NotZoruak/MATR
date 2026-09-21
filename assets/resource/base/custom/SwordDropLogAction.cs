using Avalonia;
using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions;
using MFAAvalonia.Helper;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

public class SwordDropLogAction : IMaaCustomAction
{
    // 纯色校验缺省值:该区域全部像素命中目标色时判定为非刀剑掉落画面(如内番完成对话),
    // 跳过 OCR 与打点,但保留原点击行为
    private static readonly int[] DefaultCheckRoi = [53, 257, 54, 32];
    private static readonly int[] DefaultCheckColor = [248, 244, 230];
    private static readonly int[] InitialDropColorRoi = [180, 397, 8, 30];
    private static readonly int[] InitialDropColor = [195, 13, 24];
    private static readonly int[] AnimationRoi = [131, 354, 136, 126];
    // 极化归来标志:该画面与刀剑掉落共用同一套对话框色条,动画 ROI 又读不到「极」,
    // 只能用标志图形的模板匹配区分,命中后只留档、不视为掉落
    private static readonly int[] KiwameReturnRoi = [60, 368, 121, 100];
    private const string KiwameReturnTemplate = "Common/极化归来.png";
    private const string KiwameReturnSuffix = "极化归来";
    private const double KiwameReturnThreshold = 0.9;
    // 掉落画面色条:与各掉落 node 的 ColorMatch 参数(roi [598,543,66,1] 区间 [88,48,2]~[96,56,10])保持一致,
    // 用于确认画面是否仍停留在掉落画面
    private static readonly int[] BannerRoi = [598, 543, 66, 1];
    private static readonly int[] BannerColor = [92, 52, 6];
    private const int BannerColorTolerance = 4;
    private const int DefaultCheckTolerance = 3;
    private const int InitialDropColorTolerance = 1;
    // 色条命中目标色的像素占比达到该值时判定画面仍在掉落画面
    private const double BannerMatchRatio = 0.9;
    // 打点后的等待参数:轮询间隔与总超时
    private const int BannerPollInterval = 200;
    private const int BannerTimeout = 8000;

    public string Name { get; set; } = nameof(SwordDropLogAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        var json = ActionParamHelper.Parse(args.ActionParam);
        var roi = ParseArray(json["roi"] as JArray, "刀剑掉落 OCR ROI");
        var click = ParseArray(json["click"] as JArray, "刀剑掉落点击区域");
        var task = (string?)json["task"] ?? "刀剑掉落";
        var prefix = $"[{task}]";
        // 产出掉落记录与需要留档的路径都要等画面关闭,避免同一次画面被重复处理
        var shouldWaitForClose = false;

        // 极化归来判定放在最前:该画面的对话框色条与刀剑掉落一致,若先跑纯色校验可能被直接跳过而丢失截图
        if (IsKiwameReturn(context))
        {
            // 与初掉落一样保存完整画面,但不视为掉落:不写掉落日志、不播报
            SaveKiwameReturnScreenshot(context, roi, prefix);
            LoggerHelper.Info($"{prefix} 极化归来画面，跳过刀剑掉落识别");
            shouldWaitForClose = true;
        }
        else if (IsPlainBackdrop(context, json))
        {
            // 说明性日志供排查,解析器只认词表行,该行不会计入统计
            LoggerHelper.Info($"{prefix} 非刀剑掉落画面，跳过 OCR 打点");
        }
        else
        {
            var animationKind = IsInitialDropMarker(context)
                ? SwordDropAnimationKind.InitialDrop
                : SwordDropNotificationMatcher.GetAnimationKind(ReadText(context, AnimationRoi));
            if (animationKind == SwordDropAnimationKind.Specialization)
            {
                LoggerHelper.Info($"{prefix} 特化动画，跳过刀剑掉落识别");
            }
            else if (animationKind == SwordDropAnimationKind.InitialDrop)
            {
                var text = ReadText(context, roi);
                if (TryValidateSword(text, out var swordType, out var swordName))
                {
                    SaveScreenshot(context, swordName, "初始掉落");
                    LoggerHelper.Info($"{prefix} 刀剑掉落 {swordType} {swordName}");
                    TryNotify(context, swordName, swordType);
                }
                else
                {
                    SaveScreenshot(context, "未识别刀剑", "初始掉落");
                    LoggerHelper.Warning($"{prefix} 初始掉落刀名 OCR 校验失败: {text}");
                }

                shouldWaitForClose = true;
            }
            else
            {
                ProcessOrdinaryDrop(context, roi, prefix);
                shouldWaitForClose = true;
            }
        }

        ClickRectangle(context, click);
        // 画面若一直停留在掉落画面,识别循环会反复命中本 node,导致一次掉落被重复打点与重复播报,
        // 因此打点后等待画面关闭再回主循环;画面仍在时补一次点击
        if (shouldWaitForClose)
            WaitDropScreenClosed(context, click, prefix);
        return true;
    }

    /// <summary>
    /// 极化归来标志模板匹配:命中即判定当前是极化归来画面。
    /// 该画面的标志与初印位置相近但图形不同,动画 ROI 的 OCR 读不出「极」,只能靠标志图形区分。
    /// </summary>
    private static bool IsKiwameReturn<T>(T context) where T : IMaaContext
    {
        using var image = context.GetImage();
        if (image == null)
            return false;

        var taskModel = new MaaNode
        {
            Name = "SwordDropKiwameReturn",
            Recognition = "TemplateMatch",
            Roi = KiwameReturnRoi,
            Template = [KiwameReturnTemplate],
            Threshold = new List<double> { KiwameReturnThreshold },
            GreenMask = true,
        };
        var detail = context.RunRecognition(taskModel, image);
        if (detail?.Detail == null)
            return false;

        var query = JsonConvert.DeserializeObject<MaaExtensions.RecognitionQuery>(detail.Detail);
        return query?.Best != null;
    }

    /// <summary>
    /// 极化归来画面按初掉落的方式留档:刀名可解析时以刀名命名截图,解析不出时记为未识别刀剑。
    /// 该画面不产出掉落记录、不播报。
    /// </summary>
    private static void SaveKiwameReturnScreenshot<T>(T context, int[] roi, string prefix) where T : IMaaContext
    {
        var text = ReadText(context, roi);
        if (TryValidateSword(text, out _, out var swordName))
        {
            SaveScreenshot(context, swordName, KiwameReturnSuffix);
            return;
        }

        SaveScreenshot(context, "未识别刀剑", KiwameReturnSuffix);
        LoggerHelper.Warning($"{prefix} 极化归来刀名 OCR 校验失败: {text}");
    }

    /// <summary>
    /// 打点与首次点击后轮询色条:画面仍停留在掉落画面时补一次点击,
    /// 色条消失即返回主循环。
    /// </summary>
    private static void WaitDropScreenClosed<T>(T context, int[] click, string prefix) where T : IMaaContext
    {
        var startTime = DateTime.UtcNow;

        while ((DateTime.UtcNow - startTime).TotalMilliseconds < BannerTimeout)
        {
            ActionParamHelper.ThrowIfStopping(context);
            ActionParamHelper.SleepWithStopCheck(context, BannerPollInterval);

            if (!IsDropBannerVisible(context))
            {
                LoggerHelper.Info($"{prefix} 掉落画面已关闭，结束等待");
                return;
            }

            ClickRectangle(context, click);
        }

        LoggerHelper.Warning($"{prefix} 掉落画面未关闭（等待 {BannerTimeout}ms 超时）");
    }

    /// <summary>
    /// 色条校验:与掉落 node 的 ColorMatch 区间一致,命中像素占比达到阈值时判定画面仍在掉落画面。
    /// </summary>
    private static bool IsDropBannerVisible<T>(T context) where T : IMaaContext
    {
        using var image = context.GetImage();
        if (image == null)
            return false;

        using var bitmap = image.ToBitmap();
        if (bitmap == null)
            return false;

        int x0 = BannerRoi[0];
        int y0 = BannerRoi[1];
        int width = BannerRoi[2];
        int height = BannerRoi[3];
        if (x0 < 0 || y0 < 0 || width <= 0 || height <= 0
            || x0 + width > bitmap.PixelSize.Width
            || y0 + height > bitmap.PixelSize.Height)
        {
            LoggerHelper.Warning($"掉落色条 ROI 越界: roi=[{x0},{y0},{width},{height}], 截图={bitmap.PixelSize.Width}x{bitmap.PixelSize.Height}");
            return false;
        }

        var pixelBytes = new byte[width * height * 4];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(
            pixelBytes,
            System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(
                new PixelRect(x0, y0, width, height),
                handle.AddrOfPinnedObject(),
                pixelBytes.Length,
                width * 4);
        }
        finally
        {
            handle.Free();
        }

        var matched = 0;
        for (var i = 0; i < pixelBytes.Length; i += 4)
        {
            if (SwordDropNotificationMatcher.IsColorWithinTolerance(
                    pixelBytes[i + 2],
                    pixelBytes[i + 1],
                    pixelBytes[i],
                    BannerColor[0],
                    BannerColor[1],
                    BannerColor[2],
                    BannerColorTolerance))
            {
                matched++;
            }
        }

        return matched >= width * height * BannerMatchRatio;
    }

    /// <summary>处理未识别到结果动画标记时的原有掉落识别流程。</summary>
    private static void ProcessOrdinaryDrop<T>(T context, int[] roi, string prefix) where T : IMaaContext
    {
        var text = ReadText(context, roi);
        if (TryValidateSword(text, out var swordType, out var swordName))
        {
            LoggerHelper.Info($"{prefix} 刀剑掉落 {swordType} {swordName}");
            TryNotify(context, swordName, swordType);
        }
    }

    /// <summary>按全局开关和播报名单发送刀剑掉落通知。</summary>
    private static void TryNotify<T>(T context, string swordName, string swordType) where T : IMaaContext
    {
        if (!ConfigurationManager.Current.GetValue(ConfigurationKeys.SwordDropNotificationEnabled, false))
            return;

        if (!ConfigurationManager.Current.TryGetValue(
                ConfigurationKeys.SwordDropNotificationSwords, out List<string>? swords)
            || !SwordDropNotificationMatcher.ShouldNotify(true, swords, swordName))
        {
            return;
        }

        var message = SwordDropNotificationMatcher.BuildNotificationMessage(swordType, swordName);
        ToastNotification.Show(message);
        _ = ExternalNotificationHelper.ExternalNotificationAsync(message);
        try
        {
            // 只写实时 GUI 日志，不写文件日志，避免新增工作记录解析词条。
            ActionParamHelper.ResolveOwnerProcessor(context)?.AddLog(message, writeToFileLog: false);
        }
        catch
        {
            // GUI 日志失败不应影响系统通知与任务流程。
        }
    }

    /// <summary>保存当前完整画面，截图失败不影响后续点击。</summary>
    private static void SaveScreenshot<T>(T context, string swordName, string suffix) where T : IMaaContext
    {
        try
        {
            using var image = context.GetImage();
            if (image == null)
                return;

            using var bitmap = image.ToBitmap();
            if (bitmap == null)
                return;

            var directory = Path.Combine(AppPaths.InstallRoot, "debug", "sword_drop");
            Directory.CreateDirectory(directory);
            var safeName = string.Concat(swordName.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
            var path = Path.Combine(directory, $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{safeName}_{suffix}.png");
            bitmap.Save(path);
            LoggerHelper.Info($"保存刀剑掉落截图: {path}");
        }
        catch (Exception e)
        {
            LoggerHelper.Warning($"保存刀剑掉落截图失败: {e.Message}");
        }
    }

    /// <summary>
    /// 纯色校验:指定区域内全部像素命中目标色(含容差)时判定为非刀剑掉落画面,
    /// 跳过 OCR 与打点。参数 check_roi / check_color / check_tolerance 可覆盖缺省值。
    /// </summary>
    private static bool IsPlainBackdrop<T>(T context, JObject json) where T : IMaaContext
    {
        var checkRoi = json["check_roi"] is JArray checkArray
            ? ParseArray(checkArray, "刀剑掉落纯色校验 ROI")
            : DefaultCheckRoi;
        var checkColor = json["check_color"] is JArray colorArray
            ? ParseArray(colorArray, "刀剑掉落纯色校验目标色")
            : DefaultCheckColor;
        var tolerance = Math.Max(0, (int?)json["check_tolerance"] ?? DefaultCheckTolerance);

        using var image = context.GetImage();
        if (image == null)
            return false;

        using var bitmap = image.ToBitmap();
        if (bitmap == null)
            return false;

        int x0 = checkRoi[0], y0 = checkRoi[1], w = checkRoi[2], h = checkRoi[3];
        if (w <= 0 || h <= 0 || x0 < 0 || y0 < 0 || x0 + w > bitmap.PixelSize.Width || y0 + h > bitmap.PixelSize.Height)
        {
            LoggerHelper.Warning($"纯色校验 ROI 越界: roi=[{x0},{y0},{w},{h}], 截图={bitmap.PixelSize.Width}x{bitmap.PixelSize.Height}");
            return false;
        }

        // 读取 ROI 区域像素(BGRA)
        var pixelBytes = new byte[w * h * 4];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixelBytes, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(x0, y0, w, h), handle.AddrOfPinnedObject(), pixelBytes.Length, w * 4);
        }
        finally
        {
            handle.Free();
        }

        var isPlain = true;
        for (var i = 0; i < pixelBytes.Length; i += 4)
        {
            var b = Math.Abs(pixelBytes[i] - checkColor[2]);
            var g = Math.Abs(pixelBytes[i + 1] - checkColor[1]);
            var r = Math.Abs(pixelBytes[i + 2] - checkColor[0]);
            if (b > tolerance || g > tolerance || r > tolerance)
            {
                isPlain = false;
                break;
            }
        }

        return isPlain;
    }

    /// <summary>
    /// 通过初掉落标记区域的纯色判断结果动画。
    /// 区域内所有像素都必须命中目标 RGB 颜色及其容差。
    /// </summary>
    private static bool IsInitialDropMarker<T>(T context) where T : IMaaContext
    {
        using var image = context.GetImage();
        if (image == null)
            return false;

        using var bitmap = image.ToBitmap();
        if (bitmap == null)
            return false;

        int x0 = InitialDropColorRoi[0];
        int y0 = InitialDropColorRoi[1];
        int width = InitialDropColorRoi[2];
        int height = InitialDropColorRoi[3];
        if (x0 < 0 || y0 < 0 || width <= 0 || height <= 0
            || x0 + width > bitmap.PixelSize.Width
            || y0 + height > bitmap.PixelSize.Height)
        {
            LoggerHelper.Warning($"初掉落颜色匹配 ROI 越界: roi=[{x0},{y0},{width},{height}], 截图={bitmap.PixelSize.Width}x{bitmap.PixelSize.Height}");
            return false;
        }

        var pixelBytes = new byte[width * height * 4];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(
            pixelBytes,
            System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(
                new PixelRect(x0, y0, width, height),
                handle.AddrOfPinnedObject(),
                pixelBytes.Length,
                width * 4);
        }
        finally
        {
            handle.Free();
        }

        for (var i = 0; i < pixelBytes.Length; i += 4)
        {
            if (!SwordDropNotificationMatcher.IsColorWithinTolerance(
                    pixelBytes[i + 2],
                    pixelBytes[i + 1],
                    pixelBytes[i],
                    InitialDropColor[0],
                    InitialDropColor[1],
                    InitialDropColor[2],
                    InitialDropColorTolerance))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryValidateSword(
        string text,
        out string swordType,
        out string swordName)
    {
        swordType = string.Empty;
        swordName = string.Empty;

        // 名条解析与内番对话共用同一套归一与唯一匹配；刀种由标准刀名反查刀帐得到，
        // 不依赖 OCR 把「薙刀」这类刀种读对。
        var map = FormationContext.SwordTypeMap;
        if (map == null || map.Count == 0)
        {
            map = FormationContext.LoadSwordTypeMap();
            FormationContext.SwordTypeMap = map;
        }

        if (!SwordNameResolver.TryResolve(text, map, out var resolvedName)
            || !map.TryGetValue(resolvedName, out var resolvedType))
            return false;

        swordName = resolvedName;
        swordType = resolvedType;
        return true;
    }

    private static string ReadText<T>(T context, int[] roi) where T : IMaaContext
    {
        using var image = context.GetImage();
        if (image == null)
            return string.Empty;

        var taskModel = new MaaNode
        {
            Name = "SwordDropOCR",
            Recognition = "OCR",
            Roi = roi,
        };
        var detail = context.RunRecognition(taskModel, image);
        if (detail?.Detail == null)
            return string.Empty;

        var query = JsonConvert.DeserializeObject<MaaExtensions.RecognitionQuery>(detail.Detail);
        return query?.Best?.Text ?? string.Empty;
    }

    private static void ClickRectangle<T>(T context, int[] rectangle) where T : IMaaContext
    {
        var x = rectangle[0] + (rectangle[2] > 0 ? Random.Shared.Next(rectangle[2]) : 0);
        var y = rectangle[1] + (rectangle[3] > 0 ? Random.Shared.Next(rectangle[3]) : 0);
        context.Click(x, y);
    }

    private static int[] ParseArray(JArray? value, string name)
    {
        if (value == null || value.Count != 4)
            throw new Exception($"{name}必须是 [x, y, w, h]");
        return value.ToObject<int[]>()!;
    }

}
