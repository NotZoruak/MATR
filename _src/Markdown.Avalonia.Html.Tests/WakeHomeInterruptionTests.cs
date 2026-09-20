using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class WakeHomeInterruptionTests
{
    [Fact]
    public void 唤醒本丸主枢纽使用公共中断处理并保留回本丸主线()
    {
        var pipeline = JsonNode.Parse(File.ReadAllText(FindPipelinePath()))!.AsObject();
        var next = pipeline["WH_MainHub"]!["next"]!.AsArray().Select(item => item!.GetValue<string>());
        var publicInterruptions = new[]
        {
            "[JumpBack]IsExpeditionReturn_Exp",
            "[JumpBack]IsAnnouncementPopup",
            "[JumpBack]IsTrainingLetter",
            "[JumpBack]IsLoginReward",
            "[JumpBack]IsInternalReport",
            "[JumpBack]IsAdvertisementPopup",
            "[JumpBack]IsGameIcon",
            "[JumpBack]IsLoginButton",
            "[JumpBack]IsGameUpdatePopup",
            "[JumpBack]IsInGameUpdatePopup",
            "[JumpBack]IsTrainingApplication",
            "[JumpBack]IsNetworkRequestTimeout",
            "[JumpBack]IsConnectionInterrupted"
        };
        Assert.All(publicInterruptions, interruption => Assert.Contains(interruption, next));

        var removedLocalNodes = new[]
        {
            "WH_HandleExperience",
            "WH_HandleAnnouncementPopup",
            "WH_HandleTrainingLetter",
            "WH_HandleLoginReward",
            "WH_LoginRewardClick2",
            "WH_LoginRewardClick3",
            "WH_HandleInternalReport",
            "WH_HandleLeaveTroopRecord",
            "WH_HandleAdvertisementPopup",
            "WH_HandleGameIcon",
            "WH_HandleLoginButton",
            "WH_HandleGameUpdatePopup",
            "WH_HandleInGameUpdatePopup",
            "WH_HandleNetworkRequestTimeout",
            "WH_HandleConnectionInterrupted",
            "WH_IsTrainingApplication",
            "WH_ClickTrainingApplication",
            "WH_CancelTrainingApplication"
        };
        Assert.All(removedLocalNodes, nodeName => Assert.False(pipeline.ContainsKey(nodeName), nodeName));

        Assert.Equal("WH_IsInMenu", pipeline["WH_MainHub"]!["next"]!.AsArray()[^1]!.GetValue<string>());
        Assert.NotNull(pipeline["WH_IsSwordDrop"]);
        Assert.NotNull(pipeline["WH_IsInMenu"]);
    }

    private static string FindPipelinePath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var pipelinePath = Path.Combine(directory.FullName, "assets", "resource", "base", "pipeline", "WakeHome.json");
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
                return pipelinePath;
        }

        throw new DirectoryNotFoundException("找不到 WakeHome.json 所在的仓库根目录。");
    }
}
