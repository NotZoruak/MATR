using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class DailyTaskAnchorSkipPipelineTests
{
    private static readonly string PipelinePath = FindPipelinePath();

    [Fact]
    public void 本次运行跳过使用六个路由锚点且不再引用旧动作()
    {
        var pipeline = JsonNode.Parse(File.ReadAllText(PipelinePath))!.AsObject();
        var entry = pipeline["DailyTask"]!.AsObject();
        var router = pipeline["DT_ProjectRouter"]!.AsObject();
        var candidates = router["next"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();

        AssertRoute(entry, candidates, "SyncLogistics", "DT_StepSyncLogistics");
        AssertRoute(entry, candidates, "Mix", "DT_StepMix");
        AssertRoute(entry, candidates, "Forge", "DT_StepForge");
        AssertRoute(entry, candidates, "Drill", "DT_StepDrill");
        AssertRoute(entry, candidates, "Reward", "DT_StepReward");
        AssertRoute(entry, candidates, "Mail", "DT_StepMail");

        Assert.Contains("DT_StepLoginReward", candidates);
        Assert.Contains("DT_StepWarmGift", candidates);
        Assert.Contains("DT_StepDisassemble", candidates);

        AssertSilentSkipRoute(pipeline, "DT_SyncLogisticsSkipCurrentRun", "SyncLogistics");
        AssertSilentSkipRoute(pipeline, "DT_MixSkipCurrentRun", "Mix");
        AssertSilentSkipRoute(pipeline, "DT_ForgeSkipCurrentRun", "Forge");
        AssertSilentSkipRoute(pipeline, "DT_DrillSkipCurrentRun", "Drill");
        AssertSilentSkipRoute(pipeline, "DT_RewardSkipCurrentRun", "Reward");
        AssertSilentSkipRoute(pipeline, "DT_MailSkipCurrentRun", "Mail");

        var content = File.ReadAllText(PipelinePath);
        Assert.DoesNotContain("DailyTaskStepSkipAction", content);
        Assert.DoesNotContain("DailyTaskRunResetAction", content);
    }

    private static void AssertRoute(JsonObject entry, string[] candidates, string routeName, string stepName)
    {
        Assert.NotNull(entry["anchor"]);
        Assert.Equal(stepName, entry["anchor"]!["DT_Route" + routeName]!.GetValue<string>());
        Assert.Contains("[Anchor]DT_Route" + routeName, candidates);
    }

    private static void AssertSilentSkipRoute(JsonObject pipeline, string skipName, string routeName)
    {
        var skip = pipeline[skipName]!.AsObject();

        Assert.Equal("DoNothing", skip["action"]!["type"]!.GetValue<string>());
        Assert.Equal(string.Empty, skip["anchor"]!["DT_Route" + routeName]!.GetValue<string>());
        Assert.Null(skip["focus"]);
    }

    private static string FindPipelinePath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var pipelinePath = Path.Combine(directory.FullName, "assets", "resource", "base", "pipeline", "DailyTask.json");
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) && File.Exists(pipelinePath))
                return pipelinePath;
        }

        throw new DirectoryNotFoundException("找不到 DailyTask.json 所在的仓库根目录。");
    }
}
