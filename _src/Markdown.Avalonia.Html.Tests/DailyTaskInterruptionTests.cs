using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class DailyTaskInterruptionTests
{
    [Fact]
    public void 日课入口使用公共中断处理并保留任务专属处理()
    {
        var pipeline = JsonNode.Parse(File.ReadAllText(FindPipelinePath()))!.AsObject();
        var publicInterruptions = new[]
        {
            "[JumpBack]IsExpeditionReturn_Exp",
            "[JumpBack]IsAnnouncementPopup",
            "[JumpBack]IsTrainingLetter",
            "[JumpBack]IsLoginReward",
            "[JumpBack]IsAdvertisementPopup",
            "[JumpBack]IsGameIcon",
            "[JumpBack]IsLoginButton",
            "[JumpBack]IsGameUpdatePopup",
            "[JumpBack]IsInGameUpdatePopup",
            "[JumpBack]IsInternalReport",
            "[JumpBack]IsTrainingApplication",
            "[JumpBack]IsNetworkRequestTimeout",
            "[JumpBack]IsConnectionInterrupted"
        };
        var entryNames = new[]
        {
            "DT_LoginRewardEntry",
            "DT_WarmGiftEntry",
            "DT_MixEntry",
            "DT_ForgeEntry",
            "DT_DrillEntry",
            "DT_RewardEntry",
            "DT_MailEntry",
            "DT_ReturnHomeHub"
        };

        foreach (var entryName in entryNames)
        {
            var next = pipeline[entryName]!["next"]!.AsArray().Select(item => item!.GetValue<string>());
            Assert.All(publicInterruptions, interruption => Assert.Contains(interruption, next));
        }

        var replacedLocalNodes = new[]
        {
            "DT_IsExpeditionReturn",
            "DT_IsAnnouncementPopup",
            "DT_IsTrainingLetter",
            "DT_IsLoginReward",
            "DT_LoginRewardClick2",
            "DT_LoginRewardClick3",
            "DT_IsAdvertisementPopup",
            "DT_IsGameIcon",
            "DT_IsLoginButton",
            "DT_IsGameUpdatePopup",
            "DT_IsInGameUpdatePopup",
            "DT_IsInternalReport",
            "DT_IsTrainingApplication",
            "DT_ClickTrainingApplication",
            "DT_CancelTrainingApplication",
            "DT_IsNetworkRequestTimeout",
            "DT_IsConnectionInterrupted"
        };
        Assert.All(replacedLocalNodes, nodeName => Assert.False(pipeline.ContainsKey(nodeName), nodeName));
        Assert.NotNull(pipeline["DT_IsSwordDropColor"]);
    }

    private static string FindPipelinePath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var pipelinePath = Path.Combine(directory.FullName, "assets", "resource", "base", "pipeline", "DailyTask.json");
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
                return pipelinePath;
        }

        throw new DirectoryNotFoundException("找不到 DailyTask.json 所在的仓库根目录。");
    }
}
