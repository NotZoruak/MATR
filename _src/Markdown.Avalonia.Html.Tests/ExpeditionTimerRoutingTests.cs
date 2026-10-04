using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class ExpeditionTimerRoutingTests
{
    private const string TimerNode = "E_CheckTimerExpired";

    public static IEnumerable<object[]> SynchronizedTaskCases()
    {
        yield return ["Sortie", "S_同步远征", "S_IsHome", new[] { "E_CheckTimerExpired", "S_NavigateToSortie" }, new[] { "Expedition" }];
        yield return ["RegimentBattle", "RB_同步后勤", "RB_IsTeamSelect", new[] { "RB_CloseImmediateSortiePopup", "RB_DisableAutoMarch", "RB_EnableAutoMarch", "RB_ClickAutoMarchConfirm", "RB_CaptainHub" }, new[] { "E_GoHome" }];
        yield return ["Hanapai", "HP_同步远征", "HP_IsTeamSelect", new[] { "HP_EnableAutoMarch", "HP_CaptainHub" }, new[] { "E_GoHome" }];
        yield return ["EdoCastle", "EC_同步后勤", "EC_IsTeamSelect", new[] { "EC_CaptainHub" }, new[] { "E_GoHome" }];
        yield return ["Underground", "U_同步远征", "U_CheckRoundComplete", new[] { "U_GoHomeAfterRound", "U_ContinueNextRound" }, new[] { "U_ReturnHomeAfterMarch" }];
        yield return ["TacticalTraining", "TT_同步远征", "TT_IsTeamSelect", new[] { "TT_ClickTargetTeam" }, new[] { "E_GoHome" }];
    }

    public static IEnumerable<object[]> SmartSchedulingStates()
    {
        foreach (var taskCase in SynchronizedTaskCases())
        {
            yield return [taskCase[0], taskCase[1], taskCase[2], taskCase[3], taskCase[4], false];
            yield return [taskCase[0], taskCase[1], taskCase[2], taskCase[3], taskCase[4], true];
        }
    }

    public static IEnumerable<object[]> NoSyncSchedulingStates()
    {
        foreach (var taskCase in SynchronizedTaskCases())
        {
            yield return [taskCase[0], taskCase[1], taskCase[2], false];
            yield return [taskCase[0], taskCase[1], taskCase[2], true];
        }
    }

    [Theory]
    [MemberData(nameof(SmartSchedulingStates))]
    public void 同步后勤开启时应生成与智能调度状态相符的最终路由(
        string entry,
        string syncOptionName,
        string checkSourceName,
        string[] defaultNext,
        string[] expirationNext,
        bool smartSchedulingEnabled)
    {
        var effective = ComposeEffectiveNodes(entry, syncOptionName, checkSourceName, true, smartSchedulingEnabled);
        var timerNode = effective[TimerNode]!;

        Assert.True(timerNode["enabled"]!.GetValue<bool>());
        Assert.Equal("Custom", timerNode["recognition"]!["type"]!.GetValue<string>());
        Assert.Equal("ExpeditionTimerRecognition", timerNode["recognition"]!["param"]!["custom_recognition"]!.GetValue<string>());
        Assert.Null(timerNode["action"]);
        Assert.Equal(expirationNext, ReadStringArray(timerNode["next"]));
        Assert.Null(timerNode["on_error"]);

        var expectedSourceNext = smartSchedulingEnabled
            ? GetSmartSchedulingNext(entry, syncOptionName, defaultNext)
            : GetSyncDisabledSmartNext(entry, syncOptionName, checkSourceName, defaultNext);
        Assert.Equal(expectedSourceNext, ReadStringArray(effective[checkSourceName]!["next"]));
    }

    [Theory]
    [MemberData(nameof(NoSyncSchedulingStates))]
    public void 同步后勤关闭时全局智能调度不得启用或插入计时检查(
        string entry,
        string syncOptionName,
        string checkSourceName,
        bool smartSchedulingEnabled)
    {
        var effective = ComposeEffectiveNodes(entry, syncOptionName, checkSourceName, false, smartSchedulingEnabled);
        var timerNode = effective[TimerNode]!;

        Assert.NotNull(timerNode["enabled"]);
        Assert.False(timerNode["enabled"]!.GetValue<bool>());
        Assert.Null(timerNode["on_error"]);
        Assert.DoesNotContain(TimerNode, ReadStringArray(effective[checkSourceName]!["next"]));
    }

    private static JsonObject ComposeEffectiveNodes(
        string entry,
        string syncOptionName,
        string checkSourceName,
        bool syncEnabled,
        bool smartSchedulingEnabled)
    {
        var repositoryRoot = FindRepositoryRoot();
        var pipelinePath = Path.Combine(repositoryRoot, "assets", "resource", "base", "pipeline", $"{entry}.json");
        var pipeline = JsonNode.Parse(File.ReadAllText(pipelinePath))!.AsObject();
        var sharedPipeline = JsonNode.Parse(File.ReadAllText(Path.Combine(repositoryRoot, "assets", "resource", "base", "pipeline", "Expedition.json")))!.AsObject();
        var interfaceRoot = JsonNode.Parse(File.ReadAllText(Path.Combine(repositoryRoot, "assets", "interface.json")))!.AsObject();
        var task = interfaceRoot["task"]!.AsArray()
            .Select(item => item!.AsObject())
            .Single(item => item["entry"]!.GetValue<string>() == entry);

        var effective = new JsonObject
        {
            [TimerNode] = sharedPipeline[TimerNode]!.DeepClone(),
            [checkSourceName] = pipeline[checkSourceName]!.DeepClone()
        };

        ApplyOverrides(effective, task["pipeline_override"] as JsonObject);

        // 全局选项优先于 task option，且非远征任务必须开启同步后勤才会应用该选项。
        if (smartSchedulingEnabled && (entry == "Expedition" || syncEnabled))
        {
            var smartOption = interfaceRoot["option"]!["远征智能调度"]!.AsObject();
            var enabledCase = smartOption["cases"]!.AsArray()
                .Select(item => item!.AsObject())
                .Single(item => item["name"]!.GetValue<string>() == "Yes");
            ApplyOverrides(effective, enabledCase["pipeline_override"]!.AsObject());
        }

        // 任务 option 后应用，模拟 MaaProcessor.CreateNodeAndParam 的覆盖优先级。
        if (syncEnabled)
        {
            var syncOption = interfaceRoot["option"]![syncOptionName]!.AsObject();
            var enabledCase = syncOption["cases"]!.AsArray()
                .Select(item => item!.AsObject())
                .Single(item => item["name"]!.GetValue<string>() == string.Empty);
            ApplyOverrides(effective, enabledCase["pipeline_override"]!.AsObject());
        }

        return effective;
    }

    private static void ApplyOverrides(JsonObject effectiveNodes, JsonObject? overrides)
    {
        if (overrides is null)
            return;

        foreach (var (name, value) in overrides)
        {
            if (effectiveNodes[name] is not JsonObject target || value is not JsonObject source)
                continue;

            MergeNode(target, source);
        }
    }

    private static void MergeNode(JsonObject target, JsonObject source)
    {
        foreach (var (name, value) in source)
        {
            if (target[name] is JsonObject targetObject && value is JsonObject sourceObject)
                MergeNode(targetObject, sourceObject);
            else
                target[name] = value?.DeepClone();
        }
    }

    private static string[] GetSmartSchedulingNext(string entry, string syncOptionName, string[] defaultNext)
    {
        if (entry == "Sortie")
            return defaultNext;

        var taskSyncContinuation = syncOptionName switch
        {
            "RB_同步后勤" => new[] { "RB_DisableAutoMarch", "RB_EnableAutoMarch", "RB_ClickAutoMarchConfirm", "RB_CaptainHub" },
            "HP_同步远征" => new[] { "HP_EnableAutoMarch", "HP_CaptainHub" },
            "EC_同步后勤" => new[] { "EC_IsTeamSelect" },
            "U_同步远征" => new[] { "U_GoHomeAfterRound", "U_ContinueNextRound" },
            "TT_同步远征" => new[] { "TT_ClickTargetTeam" },
            _ => throw new ArgumentOutOfRangeException(nameof(syncOptionName), syncOptionName, "未知同步后勤 option。")
        };

        return [TimerNode, .. taskSyncContinuation];
    }

    private static string[] GetSyncDisabledSmartNext(string entry, string syncOptionName, string checkSourceName, string[] defaultNext)
    {
        if (entry == "Sortie" && syncOptionName == "S_同步远征" && checkSourceName == "S_IsHome")
            return [TimerNode, "S_NavigateToSortie"];

        return defaultNext;
    }

    private static string[] ReadStringArray(JsonNode? value)
    {
        return value!.AsArray().Select(item => item!.GetValue<string>()).ToArray();
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("找不到仓库根目录。");
    }
}
