using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Helper;
using MFAAvalonia.Models;
using MFAAvalonia.Services;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>按目标类别确认可靠剪影，刷新点击仍由 pipeline 执行。</summary>
public sealed class TacticalTargetSwordRecognition : IMaaCustomRecognition
{
    private readonly SilhouetteService _service = new();
    private readonly object _sync = new();
    private string? _templateDirectory;

    public string Name { get; set; } = nameof(TacticalTargetSwordRecognition);

    public bool Analyze<T>(T context, in AnalyzeArgs args, in AnalyzeResults results) where T : IMaaContext
    {
        try
        {
            ActionParamHelper.ThrowIfStopping(context);
            var param = ActionParamHelper.Parse(args.RecognitionParam);
            var targetMode = string.Equals((string?)param["mode"], "target", StringComparison.OrdinalIgnoreCase);
            if (!targetMode && !string.Equals((string?)param["mode"], "non_target", StringComparison.OrdinalIgnoreCase))
                return false;
            var task = MaaProcessor.ResolveByTasker(context.Tasker)?.GetActiveTaskDefinition();
            var filter = task?.Option?.FirstOrDefault(option => option.Name == TacticalTargetSwordSettings.FilterOptionName);
            if (filter == null || !(filter.Index > 0 || filter.SelectedCases?.Contains("Yes") == true))
                return false;
            var names = TacticalTargetSwordSettings.ReadNames(filter.SubOptions?
                .FirstOrDefault(option => option.Name == TacticalTargetSwordSettings.ListOptionName));
            if (names.Count == 0)
                return false;

            using var image = context.GetImage();
            if (image == null || !image.TryGetEncodedData(out Stream stream))
                return false;
            using (stream)
            using (var bitmap = SKBitmap.Decode(stream))
            {
                if (bitmap == null) return false;
                using var normalized = new SKBitmap(1280, 720);
                using (var canvas = new SKCanvas(normalized))
                    canvas.DrawBitmap(bitmap, new SKRect(0, 0, 1280, 720));
                lock (_sync)
                {
                    var directory = Path.Combine(AppPaths.ResourceDirectory, "silhouette");
                    if (_templateDirectory != directory)
                    {
                        _service.LoadTemplates(directory);
                        _templateDirectory = directory;
                    }
                    var targets = _service.Templates.Where(template => names.Contains(template.Name))
                        .Select(template => template.Id).Distinct().ToArray();
                    var candidates = _service.RecognizeSwords(normalized);
                    var decision = EvaluateForMode(candidates, targets, targetMode);
                    if (decision.Message == null) return false;
                    LoggerHelper.Info(decision.Message);
                    try
                    {
                        ActionParamHelper.ResolveOwnerProcessor(context)?.AddLog(decision.Message, writeToFileLog: false);
                    }
                    catch (Exception logException)
                    {
                        LoggerHelper.Warning($"[战术强化] 剪影结果界面日志写入失败：{logException.Message}");
                    }
                    return decision.Hit;
                }
            }
        }
        catch (MaaStopException)
        {
            return false;
        }
        catch (Exception exception)
        {
            LoggerHelper.Warning($"[战术强化] 目标刀剑识别失败，继续出阵：{exception.Message}");
            return false;
        }
    }

    /// <summary>最高分至少 95%，且与第二位的差距超过对应阈值时才视为可靠。</summary>
    public static bool ShouldRefresh(IEnumerable<RecognitionResult> results, IReadOnlyCollection<int> targets)
        => Evaluate(results, targets).Refresh;

    /// <summary>可靠结果始终产生日志，是否刷新由目标名单单独决定。</summary>
    public static (bool Refresh, string? Message) Evaluate(
        IEnumerable<RecognitionResult> results, IReadOnlyCollection<int> targets)
    {
        var candidates = results.ToArray();
        if (candidates.Any(candidate => !double.IsFinite(candidate.Score) || candidate.Score < 0 || candidate.Score > 1))
            return (false, null);
        var ranked = candidates.GroupBy(candidate => candidate.Id)
            .Select(group => group.MaxBy(candidate => candidate.Score)!)
            .OrderByDescending(candidate => candidate.Score).Take(2).ToArray();
        if (ranked.Length != 2)
            return (false, null);
        var score = (decimal)ranked[0].Score;
        var gap = score - (decimal)ranked[1].Score;
        var requiredGap = score > 0.99m ? 0.015m : 0.03m;
        var reliable = score >= 0.95m && gap > requiredGap;
        if (!reliable)
            return (false, null);

        var winner = ranked[0];
        var refresh = targets.Count > 0 && !targets.Contains(winner.Id);
        var outcome = targets.Count == 0 ? "未设置可识别目标，继续出阵"
            : refresh ? "非目标刀剑，刷新剪影" : "目标刀剑，继续出阵";
        return (refresh, $"[战术强化] 当前剪影：{winner.Name}，得分 {winner.Score:P2}，{outcome}");
    }

    /// <summary>仅在结果符合当前目标类别时命中并记录一次日志。</summary>
    public static (bool Hit, string? Message) EvaluateForMode(
        IEnumerable<RecognitionResult> results, IReadOnlyCollection<int> targets, bool targetMode)
    {
        var decision = Evaluate(results, targets);
        var hit = targetMode ? !decision.Refresh : decision.Refresh;
        return hit ? (true, decision.Message) : (false, null);
    }
}
