using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Helper;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

public sealed class EdoActionSelectAction : IMaaCustomAction
{
    private const double FlagThreshold = 0.9;
    private const int ActionWaitMilliseconds = 500;
    private const int TypeTimeoutMilliseconds = 3000;
    private const int TypePollMilliseconds = 100;
    private const int ColorConfirmationCount = 3;
    private static readonly int[] ActionCountRoi = [75, 35, 50, 32];

    private static readonly IReadOnlyDictionary<string, int[]> FlagRois =
        new Dictionary<string, int[]>(StringComparer.Ordinal)
        {
            ["Start"] = [766, 511, 18, 13],
            ["P01"] = [547, 438, 18, 13],
            ["P02"] = [750, 396, 18, 13],
            ["P03"] = [1015, 390, 18, 13],
            ["P04"] = [234, 390, 18, 13],
            ["P05"] = [488, 334, 18, 13],
            ["P06"] = [679, 291, 18, 13],
            ["P07"] = [849, 300, 18, 13],
            ["P08"] = [1106, 203, 18, 13],
            ["P09"] = [355, 286, 18, 13],
            ["P10"] = [475, 225, 18, 13],
            ["P11"] = [811, 195, 18, 13],
            ["P12"] = [974, 168, 18, 13],
            ["P13"] = [137, 247, 18, 13],
            ["P14"] = [284, 190, 18, 13],
            ["P15"] = [485, 127, 18, 13],
            ["P16"] = [630, 155, 18, 13],
            ["P17"] = [896, 109, 18, 13]
        };

    private static readonly IReadOnlyDictionary<string, int[]> PointRois =
        new Dictionary<string, int[]>(FlagRois, StringComparer.Ordinal)
        {
            ["Start"] = [735, 566, 29, 14],
            ["P01"] = [515, 494, 33, 10],
            ["P02"] = [717, 452, 34, 10],
            ["P03"] = [984, 446, 31, 11],
            ["P04"] = [202, 447, 32, 10],
            ["P05"] = [457, 390, 30, 11],
            ["P06"] = [649, 347, 28, 12],
            ["P07"] = [819, 357, 29, 10],
            ["P08"] = [1076, 259, 29, 12],
            ["P09"] = [325, 341, 28, 12],
            ["P10"] = [446, 281, 28, 11],
            ["P11"] = [780, 251, 30, 11],
            ["P12"] = [944, 224, 30, 11],
            ["P13"] = [107, 303, 29, 11],
            ["P14"] = [255, 245, 28, 12],
            ["P15"] = [457, 182, 26, 12],
            ["P16"] = [600, 211, 29, 11],
            ["P17"] = [865, 164, 28, 11],
            ["Boss"] = [694, 149, 28, 11]
        };

    public string Name { get; set; } = nameof(EdoActionSelectAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            using var image = context.GetImage();
            if (image == null)
            {
                LoggerHelper.Warning("[江户潜入] 无法获取行动选择页面截图");
                return false;
            }

            var currentPoint = FindCurrentPoint(context, image);
            if (currentPoint == null)
            {
                LoggerHelper.Warning("[江户潜入] 无法通过旗帜模板确认当前位置");
                return false;
            }

            var runtimeState = LoadState();
            var recognizedActionCount = ReadActionCount(context, image, out var actionCountText);
            var remainingActions = EdoActionCountParser.Resolve(
                recognizedActionCount,
                currentPoint,
                runtimeState.RemainingActions);
            if (remainingActions < 1)
            {
                LoggerHelper.Warning(
                    $"[江户潜入] 剩余行动次数 OCR 失败或已耗尽，原始识别结果：{actionCountText ?? "<空>"}");
                return false;
            }

            if (recognizedActionCount < 0)
            {
                LoggerHelper.Warning(
                    $"[江户潜入] 剩余行动次数 OCR 失败，使用状态回退值：{remainingActions}，原始识别结果：{actionCountText ?? "<空>"}");
            }

            if (currentPoint == "Start")
            {
                if (runtimeState.CurrentPoint != "Start"
                    || runtimeState.PointTypes.Count > 0
                    || runtimeState.VisitedPoints.Count > 0)
                    runtimeState = new EdoRuntimeState();
            }

            ScanPointTypes(context, image, runtimeState);
            runtimeState.CurrentPoint = currentPoint;
            runtimeState.VisitedPoints.Add(currentPoint);
            runtimeState.RemainingActions = remainingActions;
            SaveState(runtimeState);

            var strategy = ParseStrategy(ActionParamHelper.Parse(args.ActionParam));
            var planningState = EdoPlanningState.Create(
                currentPoint,
                remainingActions,
                runtimeState.PointTypes,
                runtimeState.VisitedPoints);
            var plan = EdoRoutePlanner.Plan(planningState, strategy);
            if (plan.NextPoint == null || !PointRois.TryGetValue(plan.NextPoint, out var targetRoi))
            {
                LoggerHelper.Warning("[江户潜入] 规划器没有返回可点击目标");
                return false;
            }

            LoggerHelper.Info(
                $"[江户潜入] {currentPoint} → {plan.NextPoint}，" +
                $"Boss 概率={plan.BossSuccessProbability:P1}，" +
                $"路线={string.Join("→", plan.PlannedRoute)}");

            ActionParamHelper.SleepWithStopCheck(context, ActionWaitMilliseconds);
            context.ClickWithProgress(
                targetRoi[0] + targetRoi[2] / 2,
                targetRoi[1] + targetRoi[3] / 2);

            runtimeState.VisitedPoints.Add(plan.NextPoint);
            SaveState(runtimeState);

            if (plan.NextPoint == "Boss")
            {
                ClearState();
                return true;
            }

            var pointType = WaitForPointType(context, plan.NextPoint, targetRoi);
            if (pointType == EdoPointType.Unknown)
            {
                LoggerHelper.Warning($"[江户潜入] {plan.NextPoint} 颜色确认超时，不记录点位类型");
                return true;
            }

            runtimeState.CurrentPoint = plan.NextPoint;
            runtimeState.PointTypes[plan.NextPoint] = pointType;
            runtimeState.RemainingActions = Math.Max(remainingActions - 1, 0);
            SaveState(runtimeState);
            LoggerHelper.Info($"[江户潜入] {plan.NextPoint} 类型确认：{pointType}");
            return true;
        }
        catch (MaaStopException)
        {
            ClearState();
            LoggerHelper.Info("[江户潜入] 行动选择已停止");
            return false;
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"[江户潜入] 行动选择异常：{e.Message}");
            return false;
        }
    }

    private static EdoStrategy ParseStrategy(JObject json)
    {
        return ((string?)json["strategy"])?.ToLowerInvariant() switch
        {
            "directboss" or "直奔王点" => EdoStrategy.DirectBoss,
            "conservative" or "保守" => EdoStrategy.Conservative,
            "aggressive" or "激进" => EdoStrategy.Aggressive,
            _ => EdoStrategy.Balanced
        };
    }

    internal static int ReadActionCount<T>(T context, IMaaImageBuffer image) where T : IMaaContext
    {
        return ReadActionCount(context, image, out _);
    }

    private static int ReadActionCount<T>(
        T context,
        IMaaImageBuffer image,
        out string? text)
        where T : IMaaContext
    {
        text = context.GetText(
            ActionCountRoi[0],
            ActionCountRoi[1],
            ActionCountRoi[2],
            ActionCountRoi[3],
            image);
        return EdoActionCountParser.Parse(text);
    }

    internal static string? FindCurrentPoint<T>(T context, IMaaImageBuffer image) where T : IMaaContext
    {
        var matches = new List<string>();
        foreach (var pair in FlagRois)
        {
            var roi = pair.Value;
            if (context.TemplateMatch(
                    "Activity/江户潜入_旗帜.png",
                    image,
                    out _,
                    FlagThreshold,
                    roi[0],
                    roi[1],
                    roi[2],
                    roi[3]))
            {
                matches.Add(pair.Key);
            }
        }

        return matches.Count == 1 ? matches[0] : null;
    }

    private static void ScanPointTypes<T>(
        T context,
        IMaaImageBuffer image,
        EdoRuntimeState state)
        where T : IMaaContext
    {
        var results = PointRois.Keys.ToDictionary(
            point => point,
            _ => new List<EdoPointType>(ColorConfirmationCount),
            StringComparer.Ordinal);
        for (var attempt = 0; attempt < ColorConfirmationCount; attempt++)
        {
            ActionParamHelper.ThrowIfStopping(context);
            if (attempt > 0)
                ActionParamHelper.SleepWithStopCheck(context, TypePollMilliseconds);

            if (attempt == 0)
            {
                RecordPointTypes(context, image, results);
                continue;
            }

            using var confirmationImage = context.GetImage();
            if (confirmationImage != null)
                RecordPointTypes(context, confirmationImage, results);
        }

        foreach (var pair in PointRois)
        {
            var confirmedType = EdoPointColorClassifier.Confirm(results[pair.Key]);
            if (confirmedType != EdoPointType.Unknown)
                state.PointTypes[pair.Key] = confirmedType;
        }
    }

    private static void RecordPointTypes<T>(
        T context,
        IMaaImageBuffer image,
        IReadOnlyDictionary<string, List<EdoPointType>> results)
        where T : IMaaContext
    {
        foreach (var pair in PointRois)
            results[pair.Key].Add(ReadPointType(context, image, pair.Value));
    }

    private static EdoPointType WaitForPointType<T>(
        T context,
        string point,
        int[] roi)
        where T : IMaaContext
    {
        var attempts = TypeTimeoutMilliseconds / TypePollMilliseconds;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            ActionParamHelper.ThrowIfStopping(context);
            var results = new List<EdoPointType>(ColorConfirmationCount);
            for (var confirmation = 0; confirmation < ColorConfirmationCount; confirmation++)
            {
                if (confirmation > 0)
                    ActionParamHelper.SleepWithStopCheck(context, TypePollMilliseconds);

                using var image = context.GetImage();
                if (image == null)
                    break;

                results.Add(ReadPointType(context, image, roi));
            }

            var confirmedType = EdoPointColorClassifier.Confirm(results);
            if (confirmedType != EdoPointType.Unknown)
                return confirmedType;

            ActionParamHelper.SleepWithStopCheck(context, TypePollMilliseconds);
        }

        return EdoPointType.Unknown;
    }

    private static EdoPointType ReadPointType<T>(
        T context,
        IMaaImageBuffer image,
        int[] roi)
        where T : IMaaContext
    {
        var black = context.ColorMatch(
            31, 31, 31, 25, 25, 25, image, out _,
            threshold: 1.0, x: roi[0], y: roi[1], w: roi[2], h: roi[3],
            count: EdoPointColorClassifier.RequiredPixels);
        var purple = context.ColorMatch(
            91, 52, 146, 85, 46, 140, image, out _,
            threshold: 1.0, x: roi[0], y: roi[1], w: roi[2], h: roi[3],
            count: EdoPointColorClassifier.RequiredPixels);
        var yellow = context.ColorMatch(
            235, 150, 26, 229, 144, 20, image, out _,
            threshold: 1.0, x: roi[0], y: roi[1], w: roi[2], h: roi[3],
            count: EdoPointColorClassifier.RequiredPixels);
        return EdoPointColorClassifier.Classify(
            black ? EdoPointColorClassifier.RequiredPixels : 0,
            purple ? EdoPointColorClassifier.RequiredPixels : 0,
            yellow ? EdoPointColorClassifier.RequiredPixels : 0);
    }

    private static EdoRuntimeState LoadState()
    {
        try
        {
            if (!File.Exists(StatePath))
                return new EdoRuntimeState();

            return JsonConvert.DeserializeObject<EdoRuntimeState>(
                       File.ReadAllText(StatePath))
                   ?? new EdoRuntimeState();
        }
        catch (Exception e)
        {
            LoggerHelper.Warning($"[江户潜入] 读取行动状态失败，将重建状态：{e.Message}");
            return new EdoRuntimeState();
        }
    }

    private static void SaveState(EdoRuntimeState state)
    {
        Directory.CreateDirectory(AppPaths.ConfigDirectory);
        File.WriteAllText(
            StatePath,
            JsonConvert.SerializeObject(state, Formatting.Indented));
    }

    internal static void ClearState()
    {
        try
        {
            if (File.Exists(StatePath))
                File.Delete(StatePath);
        }
        catch (Exception e)
        {
            LoggerHelper.Warning($"[江户潜入] 清理行动状态失败：{e.Message}");
        }
    }

    private static string StatePath => Path.Combine(
        AppPaths.ConfigDirectory,
        "edo_castle_state.json");

    private sealed class EdoRuntimeState
    {
        public string CurrentPoint { get; set; } = "Start";
        public int RemainingActions { get; set; }
        public Dictionary<string, EdoPointType> PointTypes { get; set; } = new(StringComparer.Ordinal);
        public HashSet<string> VisitedPoints { get; set; } = new(StringComparer.Ordinal);
    }
}
