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
        var equipmentIndex = Array.IndexOf(postSortieNext, "S_CheckEquipmentPopup");
        var stopEquipmentIndex = Array.IndexOf(postSortieNext, "S_StopOnEquipmentPopup");
        Assert.True(equipmentIndex >= 0 && stopEquipmentIndex > equipmentIndex);

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
