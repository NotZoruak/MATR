using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Extensions.MaaFW.Custom;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.ValueType;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>解散远征部队后按名单从一号位开始重新编入刀剑。</summary>
public sealed class ExpeditionConfiguredMembersAction : IMaaCustomAction
{
    private static readonly int[][] MemberRois =
    [
        [575, 180, 217, 31], [575, 276, 217, 31], [575, 369, 218, 32],
        [576, 464, 217, 32], [576, 558, 217, 32], [576, 654, 217, 32]
    ];

    private static readonly int[][] ReplaceTargets =
    [
        [1020, 140, 33, 58], [1021, 236, 33, 57], [1020, 331, 32, 57],
        [1020, 425, 32, 58], [1019, 519, 33, 58], [1020, 614, 33, 58]
    ];

    private static readonly int[] SwordListTitleRoi = [520, 7, 230, 43];
    private static readonly int[] DisbandButtonRoi = [1151, 213, 107, 34];
    private static readonly int[] ConfirmYesRoi = [760, 441, 52, 51];
    private static readonly int[] SortButtonRoi = [939, 82, 51, 27];
    private static readonly int[] FilterButtonRoi = [805, 79, 112, 36];
    private static readonly int[] FilterTitleRoi = [496, 66, 77, 37];
    private static readonly int[] FilterConfirmRoi = [583, 604, 119, 42];

    private static readonly Dictionary<string, (int X, int Y)> TypeButtons = new(StringComparer.Ordinal)
    {
        ["短刀"] = (269, 227), ["胁差"] = (454, 229), ["打刀"] = (625, 229), ["太刀"] = (793, 228),
        ["大太刀"] = (278, 304), ["枪"] = (445, 304), ["薙刀"] = (622, 302), ["剑"] = (790, 303)
    };

    public string Name { get; set; } = nameof(ExpeditionConfiguredMembersAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            ActionParamHelper.ThrowIfStopping(context);
            var param = ActionParamHelper.Parse(args.ActionParam);
            int team = param["team"]?.ToObject<int>() ?? 0;
            var configured = SplitNames((string?)param["members"]);
            if (team is < 1 or > 5 || configured.Count == 0)
                return true;
            if (configured.Count > 6)
                return Fail(context, $"部队{team}配置了{configured.Count}把不同刀剑，超过六个队伍位置");

            foreach (var sword in configured)
            {
                var type = FormationContext.GetSwordType(sword);
                if (type == null || !TypeButtons.ContainsKey(type))
                    return Fail(context, $"配置刀名「{sword}」无法映射到有效刀种，请检查部队{team}名单");
            }

            var currentRoster = ReadRoster(context, team, stopOnError: false);
            if (IsConfiguredRoster(currentRoster, configured))
            {
                var alreadyMatched = $"部队{team}成员已符合配置，无需重编：{FormatRoster(currentRoster!)}";
                LoggerHelper.Info($"[远征编队] {alreadyMatched}");
                AddTaskLog(context, alreadyMatched);
                return true;
            }

            var mismatchMessage = currentRoster == null
                ? $"部队{team}成员无法确认与配置名单是否一致，开始编队。名单：{FormatRoster(configured)}"
                : $"部队{team}成员与配置名单不符，开始编队。名单：{FormatRoster(configured)}；当前：{FormatRoster(currentRoster)}";
            LoggerHelper.Info($"[远征编队] {mismatchMessage}");
            AddTaskLog(context, mismatchMessage);

            while (true)
            {
                ActionParamHelper.ThrowIfStopping(context);
                currentRoster = ReadRoster(context, team, stopOnError: false);
                if (currentRoster != null && currentRoster.All(string.IsNullOrWhiteSpace))
                {
                    LoggerHelper.Info($"[远征编队] 部队{team}当前为空，跳过解散并直接编队");
                }
                else if (!DisbandCurrentTeam(context, team))
                {
                    return false;
                }

                for (int index = 0; index < configured.Count; index++)
                {
                    ActionParamHelper.ThrowIfStopping(context);
                    var sword = configured[index];

                    LoggerHelper.Info($"[远征编队] 部队{team}第{index + 1}位编入「{sword}」");
                    OpenSwordSelector(context, ReplaceTargets[index]);
                    while (true)
                    {
                        ActionParamHelper.ThrowIfStopping(context);
                        if (IsPage(context, [564, 5, 152, 49], "部队选择"))
                            break;

                        if (!IsPage(context, SwordListTitleRoi, "刀剑男士选择"))
                        {
                            ActionParamHelper.SleepWithStopCheck(context, 250);
                            continue;
                        }

                        if (!PrepareSwordList(context, sword))
                        {
                            LoggerHelper.Warning($"[远征编队] 刀剑「{sword}」的排序或筛选设置未完成，将重新筛选");
                            ActionParamHelper.SleepWithStopCheck(context, 500);
                            continue;
                        }
                        if (!FindAndSelectSword(context, sword))
                        {
                            LoggerHelper.Warning($"[远征编队] 暂未能选择刀剑「{sword}」，将重新筛选");
                            ActionParamHelper.SleepWithStopCheck(context, 500);
                            continue;
                        }

                        LoggerHelper.Info($"[远征编队] 已点击「{sword}」的决定按钮，等待返回部队选择页面");
                        if (WaitForTitle(context, [564, 5, 152, 49], "部队选择", 25))
                            break;

                        LoggerHelper.Warning($"[远征编队] 点击「{sword}」后仍未返回部队选择页面，将重新筛选");
                    }
                }

                var finalRoster = ReadRoster(context, team, stopOnError: false);
                if (IsConfiguredRoster(finalRoster, configured))
                {
                    var successMessage = $"部队{team}编队完成，复核通过：{FormatRoster(finalRoster!)}";
                    LoggerHelper.Info($"[远征编队] {successMessage}");
                    AddTaskLog(context, successMessage);
                    return true;
                }

                var verifyMessage = finalRoster == null
                    ? $"部队{team}编队后无法读取成员，将重新解散并编队。名单：{FormatRoster(configured)}"
                    : $"部队{team}编队后复核不符，将重新解散并编队。名单：{FormatRoster(configured)}；当前：{FormatRoster(finalRoster)}";
                LoggerHelper.Warning($"[远征编队] {verifyMessage}");
                AddTaskLog(context, verifyMessage);
                ActionParamHelper.SleepWithStopCheck(context, 500);
            }
        }
        catch (MaaStopException)
        {
            LoggerHelper.Info("[远征编队] 任务已停止");
            return false;
        }
        catch (Exception exception)
        {
            return Fail(context, $"编队处理发生异常：{exception.Message}");
        }
    }

    private static bool DisbandCurrentTeam<T>(T context, int team) where T : IMaaContext
    {
        while (true)
        {
            ActionParamHelper.ThrowIfStopping(context);
            using (var image = context.GetImage())
            {
                if (image == null || !IsExpeditionTeamPage(context, image))
                {
                    LoggerHelper.Warning($"[远征编队] 部队{team}暂不在远征部队选择页面，等待页面返回后继续");
                    ActionParamHelper.SleepWithStopCheck(context, 500);
                    continue;
                }
                if (!HasText(context, image, DisbandButtonRoi, "解散"))
                {
                    LoggerHelper.Warning($"[远征编队] 部队{team}暂未识别到解散按钮，将重新检查");
                    ActionParamHelper.SleepWithStopCheck(context, 500);
                    continue;
                }
                ClickAndWait(context, DisbandButtonRoi[0] + DisbandButtonRoi[2] / 2, DisbandButtonRoi[1] + DisbandButtonRoi[3] / 2);
            }

            ActionParamHelper.SleepWithStopCheck(context, 500);
            using var confirmation = context.GetImage();
            if (confirmation == null)
            {
                LoggerHelper.Warning($"[远征编队] 部队{team}解散确认页面截图失败，将重新检查");
                continue;
            }
            if (!HasText(context, confirmation, ConfirmYesRoi, "是"))
            {
                LoggerHelper.Warning($"[远征编队] 部队{team}点击解散后未识别到“是”确认按钮，将重新点击解散");
                continue;
            }

            while (true)
            {
                ActionParamHelper.ThrowIfStopping(context);
                using var image = context.GetImage();
                if (image == null)
                {
                    LoggerHelper.Warning($"[远征编队] 部队{team}确认弹窗截图失败，将继续检查");
                    ActionParamHelper.SleepWithStopCheck(context, 200);
                    continue;
                }

                if (HasText(context, image, ConfirmYesRoi, "是"))
                {
                    ClickAndWait(context, ConfirmYesRoi[0] + ConfirmYesRoi[2] / 2, ConfirmYesRoi[1] + ConfirmYesRoi[3] / 2);
                    ActionParamHelper.SleepWithStopCheck(context, 500);
                    continue;
                }

                if (!IsExpeditionTeamPage(context, image))
                {
                    ActionParamHelper.SleepWithStopCheck(context, 200);
                    continue;
                }
                break;
            }

            var afterDisband = ReadRoster(context, team, stopOnError: false);
            if (afterDisband != null && afterDisband.Skip(1).All(string.IsNullOrWhiteSpace))
            {
                LoggerHelper.Info($"[远征编队] 部队{team}解散后确认二至六号位为空");
                return true;
            }

            var remainingMembers = afterDisband?.Skip(1).Where(name => !string.IsNullOrWhiteSpace(name)).ToList() ?? [];
            LoggerHelper.Warning(remainingMembers.Count > 0
                ? $"[远征编队] 部队{team}解散后仍有成员留在二至六号位：{string.Join("、", remainingMembers)}，将再次解散"
                : $"[远征编队] 部队{team}暂时无法确认二至六号位为空，将再次解散");
        }
    }

    private static List<string>? ReadRoster<T>(T context, int team, bool stopOnError = true) where T : IMaaContext
    {
        using var image = context.GetImage();
        if (image == null || !IsExpeditionTeamPage(context, image))
        {
            if (stopOnError)
                Fail(context, $"部队{team}当前不在远征部队选择页面，停止派遣");
            else
                LoggerHelper.Warning($"[远征编队] 部队{team}当前不在远征部队选择页面，无法确认解散结果");
            return null;
        }
        var members = ReadMembers(context, image);
        var unreadable = members.Where(name => !string.IsNullOrWhiteSpace(name))
            .Where(name => FormationContext.GetSwordType(name) == null)
            .ToList();
        if (unreadable.Count > 0)
        {
            if (stopOnError)
                Fail(context, $"部队{team}成员 OCR 出现刀帐外文本：{string.Join("、", unreadable)}");
            else
                LoggerHelper.Warning($"[远征编队] 部队{team}成员 OCR 出现无法确认的文本：{string.Join("、", unreadable)}");
            return null;
        }
        return members;
    }

    private static bool OpenSwordSelector<T>(T context, int[] target) where T : IMaaContext
    {
        while (true)
        {
            ActionParamHelper.ThrowIfStopping(context);
            using var image = context.GetImage();
            if (image != null && HasText(context, image, SwordListTitleRoi, "刀剑男士选择"))
                return true;
            if (image != null && IsExpeditionTeamPage(context, image))
            {
                ClickAndWait(context, target[0] + target[2] / 2, target[1] + target[3] / 2);
                continue;
            }
            LoggerHelper.Warning("[远征编队] 暂未识别到部队选择页或刀剑选择页，等待后继续");
            ActionParamHelper.SleepWithStopCheck(context, 500);
        }
    }

    private static bool PrepareSwordList<T>(T context, string sword) where T : IMaaContext
    {
        using (var image = context.GetImage())
        {
            if (image == null || !HasText(context, image, SwordListTitleRoi, "刀剑男士选择"))
                return false;
            var sortText = ReadText(context, image, SortButtonRoi);
            if (sortText.Contains("升", StringComparison.Ordinal))
            {
                ClickAndWait(context, 964, 95);
                if (!WaitForText(context, SortButtonRoi, "降"))
                    return false;
            }
            else if (!sortText.Contains("降", StringComparison.Ordinal))
            {
                return false;
            }

            ClickAndWait(context, 861, 97);
        }

        if (!WaitForTitle(context, FilterTitleRoi, "筛选"))
            return false;

        var swordType = FormationContext.GetSwordType(sword);
        if (swordType == null || !TypeButtons.TryGetValue(swordType, out var typeButton))
            return false;

        if (!ClickOnVerifiedPage(context, FilterTitleRoi, "筛选", 801, 154)
            || !ClickOnVerifiedPage(context, FilterTitleRoi, "筛选", typeButton.X, typeButton.Y)
            || !ClickOnVerifiedPage(context, FilterTitleRoi, "筛选", 1012, 154))
            return false;

        using (var image = context.GetImage())
        {
            if (image == null || !HasText(context, image, FilterTitleRoi, "筛选"))
                return false;
            var confirm = ReadText(context, image, FilterConfirmRoi);
            if (!confirm.Contains("确", StringComparison.Ordinal))
                return false;
            ClickAndWait(context, 640, 627);
        }
        return WaitForTitle(context, SwordListTitleRoi, "刀剑男士选择");
    }

    private static bool FindAndSelectSword<T>(T context, string sword) where T : IMaaContext
    {
        return ListOcrScan.ScanAndClick(
            context,
            sword,
            ListOcrScan.SwordListRoi,
            ListOcrScan.SwordScroll,
            (image, box) =>
            {
                if (context.ColorMatch(190, 190, 190, 190, 190, 190, image, out _,
                        threshold: 1.0, x: 1191, y: box[1], w: 1, h: 1, count: 1))
                    return false;
                ClickAndWait(context, 1191, box[1]);
                return true;
            },
            "ExpeditionConfiguredMembers",
            hitFilter: (image, box) => context.ColorMatch(190, 190, 190, 190, 190, 190, image, out _,
                threshold: 1.0, x: 1191, y: box[1], w: 1, h: 1, count: 1) == false);
    }

    private static bool WaitForTitle<T>(T context, int[] roi, string expected, int attempts = 5) where T : IMaaContext
    {
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            ActionParamHelper.ThrowIfStopping(context);
            using var image = context.GetImage();
            if (image != null && HasText(context, image, roi, expected))
                return true;
            ActionParamHelper.SleepWithStopCheck(context, 200);
        }
        return false;
    }

    private static bool IsPage<T>(T context, int[] roi, string expected) where T : IMaaContext
    {
        using var image = context.GetImage();
        return image != null && HasText(context, image, roi, expected);
    }

    private static bool IsConfiguredRoster(IReadOnlyList<string>? roster, IReadOnlyList<string> configured)
    {
        if (roster == null)
            return false;
        var members = roster.Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
        return members.Count == configured.Count
            && configured.All(name => members.Contains(name, StringComparer.Ordinal));
    }

    private static string FormatRoster(IEnumerable<string> names)
    {
        var members = names.Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
        return members.Count == 0 ? "（空）" : string.Join("、", members);
    }

    private static void AddTaskLog<T>(T context, string message) where T : IMaaContext
        => MaaProcessor.ResolveByTasker(context.Tasker)?.AddLog($"[本丸后勤] {message}");

    private static void ClickAndWait<T>(T context, int x, int y) where T : IMaaContext
    {
        context.Click(x, y);
        ActionParamHelper.SleepWithStopCheck(context, 500);
    }

    private static bool WaitForText<T>(T context, int[] roi, string expected) where T : IMaaContext
        => WaitForTitle(context, roi, expected);

    private static List<string> SplitNames(string? value)
        => (value ?? string.Empty).Split(new[] { '，', ',' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static bool IsExpeditionTeamPage<T>(T context, IMaaImageBuffer image) where T : IMaaContext
    {
        var title = context.GetText(564, 5, 152, 49, image) ?? string.Empty;
        return title.Contains("部队选择", StringComparison.Ordinal);
    }

    private static List<string> ReadMembers<T>(T context, IMaaImageBuffer image) where T : IMaaContext
        => MemberRois.Select(roi => (context.GetText(roi[0], roi[1], roi[2], roi[3], image) ?? string.Empty).Trim())
            .ToList();

    private static bool ClickOnVerifiedPage<T>(T context, int[] roi, string title, int x, int y) where T : IMaaContext
    {
        using var image = context.GetImage();
        if (image == null || !HasText(context, image, roi, title))
            return false;
        ClickAndWait(context, x, y);
        using var result = context.GetImage();
        return result != null && HasText(context, result, roi, title);
    }

    private static bool HasText<T>(T context, IMaaImageBuffer image, int[] roi, string expected) where T : IMaaContext
        => ReadText(context, image, roi).Contains(expected, StringComparison.Ordinal);

    private static string ReadText<T>(T context, IMaaImageBuffer image, int[] roi) where T : IMaaContext
        => context.GetText(roi[0], roi[1], roi[2], roi[3], image) ?? string.Empty;

    private static bool Fail<T>(T context, string reason) where T : IMaaContext
    {
        LoggerHelper.Warning($"[远征编队] {reason}；不调用全局停止，当前动作返回失败");
        var processor = MaaProcessor.ResolveByTasker(context.Tasker);
        if (processor == null)
        {
            LoggerHelper.Error("[远征编队] 无法定位当前任务处理器，错误已记录到运行日志");
            return false;
        }
        processor.AddLog($"[本丸后勤] 本次远征编队未完成：{reason}；不主动停止任务队列");
        return false;
    }
}
