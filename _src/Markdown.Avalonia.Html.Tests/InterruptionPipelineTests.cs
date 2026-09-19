using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class InterruptionPipelineTests
{
    [Fact]
    public void 通用中断处理文件提供无前缀的JumpBack接口()
    {
        var pipelinePath = FindPipelinePath();
        var pipeline = JsonNode.Parse(File.ReadAllText(pipelinePath))!.AsObject();
        var expectedNodes = new[]
        {
            "IsAnnouncementPopup",
            "IsTrainingLetter",
            "IsLoginReward",
            "LoginRewardClick",
            "IsExpeditionReturn_Exp",
            "IsExpeditionReturn_Title",
            "IsBattleResult_Exp",
            "IsBattleResult_Title",
            "IsAdvertisementPopup",
            "IsGameIcon",
            "IsLoginButton",
            "IsGameUpdatePopup",
            "IsInGameUpdatePopup",
            "IsInSortie",
            "IsInMenu",
            "IsInternalReport",
            "IsTrainingApplication",
            "ClickTrainingApplication",
            "CancelTrainingApplication",
            "IsNetworkRequestTimeout",
            "IsConnectionInterrupted",
            "FallbackWait"
        };

        Assert.Equal(expectedNodes.OrderBy(name => name), pipeline.Select(pair => pair.Key).OrderBy(name => name));
        var underscoredNodes = new[]
        {
            "IsExpeditionReturn_Exp",
            "IsExpeditionReturn_Title",
            "IsBattleResult_Exp",
            "IsBattleResult_Title"
        };
        Assert.DoesNotContain(
            pipeline.Select(pair => pair.Key),
            name => name.Contains('_', StringComparison.Ordinal) && !underscoredNodes.Contains(name));

        Assert.Equal(200, pipeline["IsAnnouncementPopup"]!["post_wait_freezes"]!["time"]!.GetValue<int>());
        Assert.Equal("LoginRewardClick", pipeline["IsLoginReward"]!["next"]![0]!.GetValue<string>());
        Assert.Equal(2, pipeline["LoginRewardClick"]!["repeat"]!.GetValue<int>());
        Assert.Null(pipeline["LoginRewardClick"]!["next"]);

        Assert.Equal("远征结果", pipeline["IsExpeditionReturn_Title"]!["recognition"]!["param"]!["expected"]!.GetValue<string>());
        Assert.Equal("战斗结果", pipeline["IsBattleResult_Title"]!["recognition"]!["param"]!["expected"]!.GetValue<string>());
        Assert.All(underscoredNodes, name => Assert.Null(pipeline[name]!["next"]));

        Assert.Equal("Common/合战场选中.png", pipeline["IsInSortie"]!["recognition"]!["param"]!["template"]!.GetValue<string>());
        Assert.Equal(8, pipeline["IsInSortie"]!["action"]!["param"]!["target"]![0]!.GetValue<int>());
        Assert.Null(pipeline["IsInSortie"]!["next"]);

        Assert.Equal("ClickTrainingApplication", pipeline["IsTrainingApplication"]!["next"]![0]!.GetValue<string>());
        Assert.Equal("CancelTrainingApplication", pipeline["ClickTrainingApplication"]!["next"]![0]!.GetValue<string>());
        Assert.Null(pipeline["CancelTrainingApplication"]!["next"]);

        Assert.Empty(pipeline["FallbackWait"]!.AsObject());
    }

    private static string FindPipelinePath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var pipelinePath = Path.Combine(directory.FullName, "assets", "resource", "base", "pipeline", "Interruption.json");
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
                return pipelinePath;
        }

        throw new DirectoryNotFoundException("找不到 Interruption.json 所在的仓库根目录。");
    }
}
