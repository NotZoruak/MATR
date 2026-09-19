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
        Assert.Equal("RB_FallbackWait", hub["next"]!.AsArray()[^1]!.GetValue<string>());

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

        Assert.Equal("RB_DetectWhereAmI", pipeline["RB_FallbackWait"]!["next"]![0]!.GetValue<string>());
        Assert.Equal("RestartGameAction", pipeline["RB_RestartGame"]!["action"]!["custom_action"]!.GetValue<string>());
        Assert.Equal("RB_DetectWhereAmI", pipeline["RB_RestartGame"]!["next"]![0]!.GetValue<string>());
        Assert.False(pipeline.ContainsKey("RB_IsInSortie"));
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
