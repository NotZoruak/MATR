using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class HakusanRepairPipelineTests
{
    [Fact]
    public void 白山修刀已确认路由使用重伤入口并固定前往一一()
    {
        var pipelinePath = FindPipelinePath();
        Assert.True(File.Exists(pipelinePath), "应提供 TaskHakusanRepair.json。\n" + pipelinePath);

        var pipeline = JsonNode.Parse(File.ReadAllText(pipelinePath))!.AsObject();

        Assert.Equal("THR_NavigateClickMenu", pipeline["THR_IsPreDamage"]!["next"]![0]!.GetValue<string>());
        Assert.Equal(
            ["THR_NavigateIsInMenu", "THR_NavigateClickMenu"],
            ReadNext(pipeline["THR_NavigateClickMenu"]!));
        Assert.Equal("[Anchor]Hub", pipeline["THR_NavigateClickMenu"]!["on_error"]![0]!.GetValue<string>());
        Assert.Equal(
            [600, 34, 83, 49],
            pipeline["THR_NavigateIsInMenu"]!["recognition"]!["param"]!["roi"]!
                .AsArray().Select(item => item!.GetValue<int>()).ToArray());
        Assert.Equal("THR_Hub", pipeline["THR_NavigateIsInMenu"]!["next"]![0]!.GetValue<string>());

        Assert.DoesNotContain("THR_IsTeamSelect", ReadNext(pipeline["THR_Hub"]!));
        Assert.Equal("THR_RestartGame", pipeline["THR_Hub"]!["on_error"]![0]!.GetValue<string>());
        Assert.Equal("RestartGameAction", pipeline["THR_RestartGame"]!["action"]!["custom_action"]!.GetValue<string>());
        Assert.Equal("THR_Hub", pipeline["THR_RestartGame"]!["next"]![0]!.GetValue<string>());
        Assert.Equal(
            ["THR_ClickRegion1_1", "THR_IsTeamSelect"],
            ReadNext(pipeline["THR_ClickRegion1_1"]!));

        var teamSelect = pipeline["THR_IsTeamSelect"]!;
        Assert.Equal("部队选择", teamSelect["recognition"]!["param"]!["expected"]!.GetValue<string>());
        Assert.Equal([154, 93, 1, 1], teamSelect["action"]!["param"]!["target"]!.AsArray().Select(item => item!.GetValue<int>()).ToArray());
        Assert.Equal(2, teamSelect["repeat"]!.GetValue<int>());
        Assert.Equal(["THR_IsDamagedSword", "THR_LogNoDamagedSword"], ReadNext(teamSelect));
        Assert.Equal("Common/重伤.png", pipeline["THR_IsDamagedSword"]!["recognition"]!["param"]!["template"]!.GetValue<string>());
        Assert.Equal(
            ["THR_IsDamagedSwordInSlot2", "THR_TouchDownDamagedSword"],
            ReadNext(pipeline["THR_IsDamagedSword"]!));
        Assert.Equal(
            [231, 220, 64, 54],
            pipeline["THR_IsDamagedSwordInSlot2"]!["recognition"]!["param"]!["roi"]!
                .AsArray().Select(item => item!.GetValue<int>()).ToArray());
        Assert.Equal(
            "Common/重伤.png",
            pipeline["THR_IsDamagedSwordInSlot2"]!["recognition"]!["param"]!["template"]!.GetValue<string>());
        Assert.Equal("THR_IsHakusanInSlot1", pipeline["THR_IsDamagedSwordInSlot2"]!["next"]![0]!.GetValue<string>());
        Assert.Equal(
            [74, 182, 97, 24],
            pipeline["THR_IsHakusanInSlot1"]!["recognition"]!["param"]!["roi"]!
                .AsArray().Select(item => item!.GetValue<int>()).ToArray());
        Assert.Equal(
            "白山吉光",
            pipeline["THR_IsHakusanInSlot1"]!["recognition"]!["param"]!["expected"]!.GetValue<string>());
        var touchDown = pipeline["THR_TouchDownDamagedSword"]!;
        Assert.Equal("TouchDown", touchDown["action"]!["type"]!.GetValue<string>());
        Assert.Equal("THR_IsDamagedSword", touchDown["action"]!["param"]!["target"]!.GetValue<string>());
        Assert.Equal("THR_TouchMoveDamagedSwordToSlot2", touchDown["next"]![0]!.GetValue<string>());

        var touchMove = pipeline["THR_TouchMoveDamagedSwordToSlot2"]!;
        Assert.Equal("TouchMove", touchMove["action"]!["type"]!.GetValue<string>());
        Assert.Equal(
            [273, 236, 1, 1],
            touchMove["action"]!["param"]!["target"]!.AsArray().Select(item => item!.GetValue<int>()).ToArray());
        Assert.Equal(500, touchMove["post_delay"]!.GetValue<int>());
        Assert.Equal("THR_TouchUpDamagedSwordToSlot2", touchMove["next"]![0]!.GetValue<string>());

        var touchUp = pipeline["THR_TouchUpDamagedSwordToSlot2"]!;
        Assert.Equal("TouchUp", touchUp["action"]!["type"]!.GetValue<string>());
        Assert.Equal("THR_IsDamagedSword", touchUp["next"]![0]!.GetValue<string>());
        Assert.Equal("[Anchor]PublicFlowDone", pipeline["THR_LogNoDamagedSword"]!["next"]![0]!.GetValue<string>());
        Assert.Equal(
            "special:[白山修刀] 未检测到重伤刀剑男士，结束白山修刀流程",
            pipeline["THR_LogNoDamagedSword"]!["focus"]!["Node.Action.Succeeded"]!["content"]!.GetValue<string>());
    }

    [Fact]
    public void 修刀公共流程统一从公共流程出口返回()
    {
        var repositoryRoot = FindRepositoryRoot();
        var pipelineDirectory = Path.Combine(repositoryRoot, "assets", "resource", "base", "pipeline");
        var pipelineFiles = new[]
        {
            "Repair.json",
            "TaskHakusanRepair.json",
            "TaskFlowerBrush.json",
            "Sortie.json",
            "Underground.json",
            "TacticalTraining.json",
            "EdoCastle.json",
            "Hanapai.json",
            "Harvest.json",
            "RegimentBattle.json"
        };

        foreach (var fileName in pipelineFiles)
        {
            var content = File.ReadAllText(Path.Combine(pipelineDirectory, fileName));
            Assert.DoesNotContain("[Anchor]RepairDone", content, StringComparison.Ordinal);
            Assert.DoesNotContain("\"RepairDone\":", content, StringComparison.Ordinal);
            Assert.Contains("PublicFlowDone", content, StringComparison.Ordinal);
        }
    }

    private static string[] ReadNext(JsonNode node) => node["next"]!.AsArray().Select(item => item!.GetValue<string>()).ToArray();

    private static string FindPipelinePath()
    {
        return Path.Combine(FindRepositoryRoot(), "assets", "resource", "base", "pipeline", "TaskHakusanRepair.json");
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
