using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Helper;
using System;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>
/// 联队战部队交替：读取剩余轮次与队伍序列，判断是否需要换队；需要时打开换队面板、选中目标部队并确认。
///
/// 判断与执行合并在同一个动作里，是因为两者都要读取同一块轮次 OCR 结果与跨轮次的当前部队状态：
/// 拆成自定义识别加自定义动作会重复取图、重复 OCR，而且「不需要换队」只能通过识别失败表达，
/// 无法把「本次无需换队」与「轮次读不到」统一落到同一个后续分支。
/// </summary>
public class TeamSwitchCheckAction : IMaaCustomAction
{
    public string Name { get; set; } = nameof(TeamSwitchCheckAction);

    /// <summary>换队面板里五支部队按钮的点击位置。</summary>
    private static readonly int[][] TeamButtons =
    [
        [850, 479, 1, 1],
        [1007, 475, 1, 1],
        [1167, 473, 1, 1],
        [931, 608, 1, 1],
        [1077, 606, 1, 1],
    ];

    /// <summary>打开换队面板的按钮。</summary>
    private static readonly int[] SwitchPanelButton = [841, 559, 108, 69];

    /// <summary>换队确认按钮。</summary>
    private static readonly int[] ConfirmButton = [931, 353, 1, 1];

    /// <summary>剩余轮次 OCR 范围。</summary>
    private static readonly int[] RoundRoi = [114, 39, 28, 25];

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            var json = ActionParamHelper.Parse(args.ActionParam);
            var teamConfig = (string?)json["team_config"] ?? "111111111";
            var initialTeam = int.TryParse((string?)json["initial_team"], out var parsedTeam)
                ? parsedTeam
                : 1;

            if (!IsValidTeamConfig(teamConfig))
            {
                LoggerHelper.Error($"[TeamSwitch] 队伍序列必须是 9 位 1-5 的数字，当前: {teamConfig}");
                return false;
            }

            var remaining = ReadRemaining(context);
            if (remaining is < 1 or > 9)
            {
                LoggerHelper.Info("[TeamSwitch] 未识别到剩余轮次，本次不换队");
                return true;
            }

            if (!TeamSwitchState.ShouldSwitch(teamConfig, remaining, initialTeam))
            {
                LoggerHelper.Info($"[TeamSwitch] 第{11 - remaining}轮无需换队");
                return true;
            }

            var nextRound = 11 - remaining;
            var teamIndex = teamConfig[9 - remaining] - '1';
            LoggerHelper.Info($"[TeamSwitch] 剩余{remaining}轮（即将第{nextRound}轮）→ 部队{teamIndex + 1}");

            // 打开换队面板，与旧链路里 ClickSwitchTeam 的双击等价
            ActionParamHelper.SleepWithStopCheck(context, 200);
            ClickCenter(context, SwitchPanelButton);
            ActionParamHelper.SleepWithStopCheck(context, 300);
            ClickCenter(context, SwitchPanelButton);
            ActionParamHelper.SleepWithStopCheck(context, 200);

            // 双击目标部队后确认
            ClickCenter(context, TeamButtons[teamIndex]);
            ActionParamHelper.SleepWithStopCheck(context, 500);
            ClickCenter(context, TeamButtons[teamIndex]);
            ActionParamHelper.SleepWithStopCheck(context, 500);
            ClickCenter(context, ConfirmButton);
            ActionParamHelper.SleepWithStopCheck(context, 500);

            TeamSwitchState.SetCurrentTeam(teamIndex + 1);

            LoggerHelper.Info($"[TeamSwitch] 换队完成: 第{nextRound}轮 → 部队{teamIndex + 1}");
            return true;
        }
        catch (MaaStopException)
        {
            LoggerHelper.Info("[TeamSwitch] 手动停止");
            return false;
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"[TeamSwitch] Error: {e.Message}");
            return false;
        }
    }

    /// <summary>校验队伍序列：9 位且每位为 1-5。</summary>
    private static bool IsValidTeamConfig(string teamConfig)
    {
        if (teamConfig.Length != 9)
            return false;

        foreach (var c in teamConfig)
        {
            if (c is < '1' or > '5')
                return false;
        }

        return true;
    }

    /// <summary>点击矩形中心，与 pipeline 里 target 的取值方式一致。</summary>
    private static void ClickCenter<T>(T context, int[] rect) where T : IMaaContext
        => context.Click(rect[0] + rect[2] / 2, rect[1] + rect[3] / 2);

    /// <summary>读取剩余轮次，OCR 抖动时最多重试 8 次，失败返回 -1。</summary>
    private static int ReadRemaining<T>(T context) where T : IMaaContext
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            ActionParamHelper.ThrowIfStopping(context);

            using var image = context.GetImage();
            if (image == null)
            {
                ActionParamHelper.SleepWithStopCheck(context, 400);
                continue;
            }

            var text = context.GetText(RoundRoi[0], RoundRoi[1], RoundRoi[2], RoundRoi[3], image)
                .Replace("O", "0")
                .Replace("o", "0")
                .Replace("l", "1")
                .Replace("I", "1")
                .Replace("Z", "2")
                .Replace("S", "5");

            if (int.TryParse(text, out var digit) && digit is >= 1 and <= 9)
            {
                LoggerHelper.Info($"[TeamSwitch] 轮次 OCR 结果: '{text}'（第{attempt + 1}次）");
                return digit;
            }

            LoggerHelper.Info($"[TeamSwitch] 轮次 OCR 结果: '{text}'（第{attempt + 1}次）无法解析");
            ActionParamHelper.SleepWithStopCheck(context, 400);
        }

        return -1;
    }
}
