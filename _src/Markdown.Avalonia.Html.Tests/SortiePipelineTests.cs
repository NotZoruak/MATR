using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class SortiePipelineTests
{
    [Fact]
    public void 常驻作战主枢纽挂载公共中断链但保留活动页专属识别()
    {
        var pipelinePath = FindPipelinePath();
        Assert.True(File.Exists(pipelinePath), "应提供 Sortie.json。\n" + pipelinePath);

        var pipeline = JsonNode.Parse(File.ReadAllText(pipelinePath))!.AsObject();
        var hub = pipeline["S_DetectWhereAmI"]!;
        var next = hub["next"]!.AsArray().Select(item => item!.GetValue<string>()).ToArray();

        var expectedInterruptions = new[]
        {
            "[JumpBack]IsBattleResult_Exp",
            "[JumpBack]IsBattleResult_Title",
            "[JumpBack]IsExpeditionReturn_Exp",
            "[JumpBack]IsExpeditionReturn_Title",
            "[JumpBack]IsInMenu",
            "[JumpBack]IsAnnouncementPopup",
            "[JumpBack]IsTrainingLetter",
            "[JumpBack]IsLoginReward",
            "[JumpBack]IsAdvertisementPopup",
            "[JumpBack]IsGameIcon",
            "[JumpBack]IsLoginButton",
            "[JumpBack]IsGameUpdatePopup",
            "[JumpBack]IsInGameUpdatePopup",
            "[JumpBack]IsInternalReport",
            "[JumpBack]IsNetworkRequestTimeout",
            "[JumpBack]IsConnectionInterrupted",
        };

        Assert.All(expectedInterruptions, interruption => Assert.Contains(interruption, next));
        Assert.Contains("S_IsInActivity", next);
        Assert.Equal("[JumpBack]FallbackWait", next[^1]);

        var formation = pipeline["S_IsFormationSelect"]!.AsObject();
        Assert.Equal("Click", formation["action"]!["type"]!.GetValue<string>());
        Assert.Equal(2, formation["repeat"]!.GetValue<int>());
        Assert.False(pipeline.ContainsKey("S_ClickFormation1"));
        Assert.False(pipeline.ContainsKey("S_ClickFormation2"));

        var team = pipeline["S_IsTeamSelect"]!.AsObject();
        Assert.Equal("Click", team["action"]!["type"]!.GetValue<string>());
        Assert.Equal(2, team["repeat"]!.GetValue<int>());
        Assert.Contains("S_CaptainHub", team["next"]!.AsArray().Select(item => item!.GetValue<string>()));
        Assert.False(pipeline.ContainsKey("S_ClickTeam"));

        var captainHub = pipeline["S_CaptainHub"]!.AsObject();
        Assert.Equal("S_DragCaptain", captainHub["next"]![0]!.GetValue<string>());
        Assert.False(pipeline["S_DragCaptain"]!["enabled"]!.GetValue<bool>());

        var teamNext = team["next"]!.AsArray().Select(item => item!.GetValue<string>()).ToArray();
        Assert.DoesNotContain("S_CheckEquipmentPopup", teamNext);
        Assert.DoesNotContain("S_StopOnEquipmentPopup", teamNext);

        var postSortieNext = pipeline["S_PostSortieHub"]!["next"]!.AsArray().Select(item => item!.GetValue<string>()).ToArray();
        var stopEquipmentIndex = Array.IndexOf(postSortieNext, "S_StopOnEquipmentPopup");
        Assert.DoesNotContain("IsEquipmentShortagePopup", postSortieNext);
        Assert.True(stopEquipmentIndex >= 0);
        Assert.False(pipeline.ContainsKey("S_CheckEquipmentPopup"));

        Assert.DoesNotContain(
            pipeline,
            pair => pair.Value?["action"]?["custom_action"]?.GetValue<string>() == "LogAction");
        Assert.DoesNotContain(
            pipeline,
            pair => pair.Value?["action"]?["custom_action"]?.GetValue<string>() == "GuiLogAction");
        Assert.Equal(
            "file",
            pipeline["S_SortieSuccess"]!["focus"]!["Node.Action.Succeeded"]!["display"]!.GetValue<string>());
        Assert.Equal(
            "[常驻作战] 道中撤退",
            pipeline["S_MidRetreat_E4_R1_1"]!["focus"]!["Node.Action.Succeeded"]!["content"]!.GetValue<string>());
        Assert.Equal(
            "file",
            pipeline["S_MidRetreat_E4_R1_1"]!["focus"]!["Node.Action.Succeeded"]!["display"]!.GetValue<string>());
        Assert.Equal(
            "file",
            pipeline["S_Boss_E4_R1"]!["focus"]!["Node.Action.Succeeded"]!["display"]!.GetValue<string>());
        Assert.Equal(
            "file",
            pipeline["S_IsIsekaiConfirmPurchase"]!["focus"]!["Node.Action.Succeeded"]!["display"]!.GetValue<string>());
        Assert.Null(pipeline["S_LogStopOnDamage"]!["action"]);
        Assert.Equal(
            "special:[重伤检测] 检测到刀剑男士重伤，停止任务",
            pipeline["S_LogStopOnDamage"]!["focus"]!["Node.Action.Succeeded"]!["content"]!.GetValue<string>());
        var damageDisplays = pipeline["S_LogStopOnDamage"]!["focus"]!["Node.Action.Succeeded"]!["display"]!
            .AsArray()
            .Select(item => item!.GetValue<string>())
            .ToArray();
        Assert.Contains("log", damageDisplays);
        Assert.Contains("notification", damageDisplays);

        var hubAnchors = pipeline["S_DetectWhereAmI"]!["anchor"]!.AsObject();
        Assert.Equal("S_DetectWhereAmI", hubAnchors["GrindDone"]!.GetValue<string>());
        Assert.Equal("TF_Hub", pipeline["S_FatigueCheck"]!["on_error"]![0]!.GetValue<string>());
        Assert.DoesNotContain(pipeline.Select(pair => pair.Key), name => name.StartsWith("SF_", StringComparison.Ordinal));

        var firstJumpBack = Array.FindIndex(next, item => item.StartsWith("[JumpBack]", StringComparison.Ordinal));
        Assert.True(firstJumpBack >= 0);
        Assert.All(next[firstJumpBack..^1], item => Assert.StartsWith("[JumpBack]", item));

        Assert.False(pipeline.ContainsKey("S_IsBattleResult_Exp"));
        Assert.False(pipeline.ContainsKey("S_IsBattleResult_Title"));
        Assert.False(pipeline.ContainsKey("S_IsMenuDirectory"));
        Assert.False(pipeline.ContainsKey("S_IsAnnouncementPopup"));
        Assert.False(pipeline.ContainsKey("S_IsConnectionInterrupted"));
    }

    private static string FindPipelinePath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var pipelinePath = Path.Combine(directory.FullName, "assets", "resource", "base", "pipeline", "Sortie.json");
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
                return pipelinePath;
        }

        throw new DirectoryNotFoundException("找不到 Sortie.json 所在的仓库根目录。");
    }
}
