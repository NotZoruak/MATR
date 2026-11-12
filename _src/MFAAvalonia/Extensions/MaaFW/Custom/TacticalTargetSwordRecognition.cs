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

/// <summary>可靠识别为非目标刀剑时命中，刷新点击仍由 pipeline 执行。</summary>
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
                    var decision = Evaluate(candidates, targets);
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
                    return decision.Refresh;
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

    /// <summary>达到 90% 时需领先至少 1 个百分点，85% 至 90% 以下时需领先至少 5 个百分点。</summary>
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
        var reliable = (score >= 0.90m && gap >= 0.01m)
            || (score >= 0.85m && score < 0.90m && gap >= 0.05m);
        if (!reliable)
            return (false, null);

        var winner = ranked[0];
        var refresh = targets.Count > 0 && !targets.Contains(winner.Id);
        var outcome = targets.Count == 0 ? "未设置可识别目标，继续出阵"
            : refresh ? "非目标刀剑，刷新剪影" : "目标刀剑，继续出阵";
        return (refresh, $"[战术强化] 当前剪影：{winner.Name}，可信度 {winner.Score:P2}，{outcome}");
    }
}
