using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class RegimentBattlePipelineTests
{
    [Fact]
    public void 联队战骨架从入口进入主枢纽并挂载公共中断处理()
    {
        var pipelinePath = FindPipelinePath();
        Assert.True(File.Exists(pipelinePath), "应提供 RegimentBattle.json。\n" + pipelinePath);

        var pipeline = JsonNode.Parse(File.ReadAllText(pipelinePath))!.AsObject();
        Assert.Equal("RB_DetectWhereAmI", pipeline["RegimentBattle"]!["next"]![0]!.GetValue<string>());

        var hub = pipeline["RB_DetectWhereAmI"]!;
        Assert.Equal(0, hub["pre_delay"]!.GetValue<int>());
        Assert.Equal(120000, hub["timeout"]!.GetValue<int>());
        Assert.Equal("RB_RestartGame", hub["on_error"]![0]!.GetValue<string>());
        Assert.Equal("[JumpBack]FallbackWait", hub["next"]!.AsArray()[^1]!.GetValue<string>());

        var expectedInterruptions = new[]
        {
            "[JumpBack]IsAnnouncementPopup",
            "[JumpBack]IsTrainingLetter",
            "[JumpBack]IsLoginReward",
            "[JumpBack]IsExpeditionReturn_Exp",
            "[JumpBack]IsExpeditionReturn_Title",
            "[JumpBack]IsBattleResult_Exp",
            "[JumpBack]IsBattleResult_Title",
            "[JumpBack]IsAdvertisementPopup",
            "[JumpBack]IsGameIcon",
            "[JumpBack]IsLoginButton",
            "[JumpBack]IsGameUpdatePopup",
            "[JumpBack]IsInGameUpdatePopup",
            "[JumpBack]IsInSortie",
            "[JumpBack]IsInternalReport",
            "[JumpBack]IsNetworkRequestTimeout",
            "[JumpBack]IsConnectionInterrupted"
        };
        var next = hub["next"]!.AsArray().Select(item => item!.GetValue<string>());
        Assert.All(expectedInterruptions, interruption => Assert.Contains(interruption, next));

        Assert.False(pipeline.ContainsKey("RB_FallbackWait"));
        Assert.Equal("RestartGameAction", pipeline["RB_RestartGame"]!["action"]!["custom_action"]!.GetValue<string>());
        Assert.Equal("RB_DetectWhereAmI", pipeline["RB_RestartGame"]!["next"]![0]!.GetValue<string>());
        Assert.False(pipeline.ContainsKey("RB_IsInSortie"));
    }

    [Fact]
    public void 海陆联队自动行军开关控制常规与远征计时成功路径()
    {
        var pipelinePath = FindPipelinePath();
        var repositoryRoot = Directory.GetParent(Path.GetDirectoryName(pipelinePath)!)!.Parent!.Parent!.Parent!.FullName;
        var pipeline = JsonNode.Parse(File.ReadAllText(pipelinePath))!.AsObject();
        var interfaceConfig = JsonNode.Parse(File.ReadAllText(Path.Combine(repositoryRoot, "assets", "interface.json")))!.AsObject();

        var task = interfaceConfig["task"]!.AsArray()
            .Select(item => item!.AsObject())
            .Single(item => item["name"]!.GetValue<string>() == "海陆联队");
        Assert.Contains("RB_自动行军", task["option"]!.AsArray().Select(item => item!.GetValue<string>()));

        var option = interfaceConfig["option"]!["RB_自动行军"]!.AsObject();
        Assert.Equal("checkbox", option["type"]!.GetValue<string>());
        Assert.Empty(option["default_case"]!.AsArray());

        var teamSelectNext = pipeline["RB_IsTeamSelect"]!["next"]!.AsArray()
            .Select(item => item!.GetValue<string>())
            .ToArray();
        var expectedAutoMarchNext = new[]
        {
            "RB_DisableAutoMarch",
            "RB_EnableAutoMarch",
            "RB_ClickAutoMarchConfirm",
            "RB_CaptainHub"
        };
        Assert.Equal(expectedAutoMarchNext, teamSelectNext);
        Assert.True(pipeline["RB_DisableAutoMarch"]!["enabled"]!.GetValue<bool>());
        Assert.False(pipeline["RB_EnableAutoMarch"]!["enabled"]!.GetValue<bool>());
        Assert.Equal(
            new[] { 440, 584, 110, 38 },
            pipeline["RB_ClickAutoMarchConfirm"]!["recognition"]!["param"]!["roi"]!.AsArray()
                .Select(item => item!.GetValue<int>())
                .ToArray());
        var delegateExpected = Assert.IsType<JsonArray>(
            pipeline["RB_ClickAutoMarchConfirm"]!["recognition"]!["param"]!["expected"]);
        Assert.Equal(new[] { "委托" }, delegateExpected.Select(item => item!.GetValue<string>()).ToArray());

        var enabledCase = option["cases"]![0]!["pipeline_override"]!.AsObject();
        Assert.False(enabledCase["RB_DisableAutoMarch"]!["enabled"]!.GetValue<bool>());
        Assert.True(enabledCase["RB_EnableAutoMarch"]!["enabled"]!.GetValue<bool>());
        Assert.Equal(
            new[] { 713, 580, 150, 50 },
            enabledCase["RB_ClickAutoMarchConfirm"]!["recognition"]!["param"]!["roi"]!.AsArray()
                .Select(item => item!.GetValue<int>())
                .ToArray());

        var syncOption = interfaceConfig["option"]!["RB_同步后勤"]!.AsObject();
        var timerOverride = syncOption["cases"]![0]!["pipeline_override"]!["E_CheckTimerExpired"]!.AsObject();
        Assert.Equal(expectedAutoMarchNext, timerOverride["next"]!.AsArray().Select(item => item!.GetValue<string>()).ToArray());
        Assert.Equal("E_GoHome", timerOverride["on_error"]![0]!.GetValue<string>());
    }

    private static string FindPipelinePath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var pipelinePath = Path.Combine(directory.FullName, "assets", "resource", "base", "pipeline", "RegimentBattle.json");
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
                return pipelinePath;
        }

        throw new DirectoryNotFoundException("找不到 RegimentBattle.json 所在的仓库根目录。");
    }
}
