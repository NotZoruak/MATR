using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Extensions.MaaFW.Custom;
using MFAAvalonia.Helper;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>识别当前远征队是否与配置名单完全一致。</summary>
public sealed class ExpeditionConfiguredMembersRecognition : IMaaCustomRecognition
{
    private static readonly int[][] MemberRois =
    [
        [575, 180, 217, 31], [575, 276, 217, 31], [575, 369, 218, 32],
        [576, 464, 217, 32], [576, 558, 217, 32], [576, 654, 217, 32]
    ];

    public string Name { get; set; } = nameof(ExpeditionConfiguredMembersRecognition);

    public bool Analyze<T>(T context, in AnalyzeArgs args, in AnalyzeResults results) where T : IMaaContext
    {
        try
        {
            ActionParamHelper.ThrowIfStopping(context);
            var param = ActionParamHelper.Parse(args.RecognitionParam);
            var team = param["team"]?.ToObject<int>() ?? 0;
            var configured = SplitNames((string?)param["members"]);
            if (team is < 1 or > 5 || configured.Count == 0)
                return true;

            using var image = context.GetImage();
            if (image == null || !IsExpeditionTeamPage(context, image))
                return false;

            var current = ReadMembers(context, image);
            var unreadable = current.Where(name => !string.IsNullOrWhiteSpace(name))
                .Where(name => FormationContext.ResolveSwordName(name) == null)
                .ToList();
            if (unreadable.Count > 0)
            {
                LoggerHelper.Warning($"[远征编队] 部队{team}成员 OCR 出现刀帐外文本：{string.Join("、", unreadable)}");
                return false;
            }

            var currentMembers = current
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(FormationContext.ResolveSwordName)
                .Where(name => name != null)
                .Cast<string>()
                .ToList();
            bool matches = currentMembers.Count == configured.Count
                && configured.All(name => currentMembers.Contains(name, StringComparer.Ordinal));
            if (matches)
            {
                LoggerHelper.Info($"[远征编队] 部队{team}成员符合配置");
            }
            else
            {
                var currentText = currentMembers.Count == 0 ? "（空）" : string.Join("、", currentMembers);
                var configuredText = string.Join("、", configured);
                var message = $"部队{team}成员与配置名单不符，开始编队。名单：{configuredText}；当前：{currentText}";
                LoggerHelper.Info($"[远征编队] {message}");
            }
            return matches;
        }
        catch (Exception exception)
        {
            LoggerHelper.Warning($"[远征编队] 成员识别不确定，交由安全复核处理：{exception.Message}");
            return false;
        }
    }

    internal static List<string> SplitNames(string? value)
        => (value ?? string.Empty).Split(new[] { '，', ',' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    internal static bool IsExpeditionTeamPage<T>(T context, MaaFramework.Binding.Buffers.IMaaImageBuffer image) where T : IMaaContext
    {
        var title = context.GetText(564, 5, 152, 49, image) ?? string.Empty;
        return title.Contains("部队选择", StringComparison.Ordinal);
    }

    internal static List<string> ReadMembers<T>(T context, MaaFramework.Binding.Buffers.IMaaImageBuffer image) where T : IMaaContext
        => MemberRois.Select(roi => (context.GetText(roi[0], roi[1], roi[2], roi[3], image) ?? string.Empty).Trim())
            .ToList();
}
