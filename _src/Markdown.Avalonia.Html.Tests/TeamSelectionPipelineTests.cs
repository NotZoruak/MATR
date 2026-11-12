using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class TeamSelectionPipelineTests
{
    public static IEnumerable<object[]> 普通选队入口()
    {
        yield return ["Sortie", "S_IsTeamSelect", "S_ConfirmTeam", "S_选择部队"];
        yield return ["Underground", "U_ClickTeam", "U_ConfirmTeam", "U_选择部队"];
        yield return ["RegimentBattle", "RB_IsTeamSelect", "RB_ConfirmTeam", "RB_选择部队"];
        yield return ["Hanapai", "HP_IsTeamSelect", "HP_ConfirmTeam", "HP_选择部队"];
        yield return ["EdoCastle", "EC_IsTeamSelect", "EC_ConfirmTeam", "EC_选择部队"];
        yield return ["Harvest", "HV_IsTeamSelect", "HV_ConfirmTeam", "HV_选择部队"];
        yield return ["FlowerBrush", "FB_ClickTeam", "FB_ConfirmTeam", "FB_选择部队"];
        yield return ["DailyTask", "DT_DrillSelectTeam", "DT_DrillConfirmTeam", "DT_演练部队"];
        yield return ["TacticalTraining", "TT_ClickTargetTeam", "TT_ConfirmTargetTeam", "TT_选择部队"];
        yield return ["TaskFlowerBrush", "TF_IsTeamSelect", "TF_ConfirmTeam", "S_选择部队"];
        yield return ["TaskHakusanRepair", "THR_IsTeamSelect", "THR_ConfirmTeam", "S_选择部队"];
    }

    [Theory]
    [MemberData(nameof(普通选队入口))]
    public void 未选中检测必须先于业务分支且跟随五队选项(string file, string click, string confirm, string option)
    {
        var pipeline = ReadPipeline(file);
        Assert.Equal(confirm, pipeline[click]!["next"]![0]!.GetValue<string>());
        var check = pipeline[confirm]!;
        Assert.Equal("ColorMatch", check["recognition"]!["type"]!.GetValue<string>());
        Assert.Equal(new[] { click }, Strings(check["next"]!));
        var hub = file switch
        {
            "TaskFlowerBrush" => "TF_Hub",
            "TaskHakusanRepair" => "THR_Hub",
            _ => click.Split('_')[0] + "_DetectWhereAmI",
        };
        if (pipeline[click]!["recognition"] is null)
            Assert.Null(check["on_error"]);
        else
            Assert.Equal(new[] { hub }, Strings(check["on_error"]!));
        Assert.Equal(new[] { 165, 14, 14 }, Numbers(check["recognition"]!["param"]!["lower"]!));
        Assert.Equal(new[] { 185, 34, 34 }, Numbers(check["recognition"]!["param"]!["upper"]!));

        var cases = ReadInterface()["option"]![option]!["cases"]!.AsArray();
        for (var index = 0; index < 5; index++)
        {
            var roi = cases[index]!["pipeline_override"]![confirm]!["recognition"]!["param"]!["roi"]!;
            Assert.Equal(ExpectedRoi(index), Numbers(roi));
        }
    }

    [Fact]
    public void 公共刷花和白山修刀验证应随所有调用方选队选项更新()
    {
        foreach (var option in ReadInterface()["option"]!.AsObject())
        {
            if (option.Value?["cases"] is not JsonArray cases) continue;
            for (var index = 0; index < cases.Count; index++)
            {
                if (cases[index]?["pipeline_override"] is not JsonObject overrides) continue;
                foreach (var (click, confirm) in new[] { ("TF_IsTeamSelect", "TF_ConfirmTeam"), ("THR_IsTeamSelect", "THR_ConfirmTeam") })
                {
                    if (!overrides.ContainsKey(click)) continue;
                    Assert.Equal(ExpectedRoi(index), Numbers(overrides[confirm]!["recognition"]!["param"]!["roi"]!));
                }
            }
        }
    }

    [Fact]
    public void 战术强化必须先确认当前尝试部队再决定是否尝试下一队()
    {
        var pipeline = ReadPipeline("TacticalTraining");
        for (var team = 1; team <= 5; team++)
        {
            var click = $"TT_TryTeam{team}";
            var confirm = $"TT_ConfirmTryTeam{team}";
            var next = Strings(pipeline[click]!["next"]!);
            Assert.Equal(confirm, next[0]);
            var businessNext = next.Skip(1).ToArray();
            // 兼容通过空操作路由进入队长判断的既有写法，验证只约束选队后的业务顺序。
            if (businessNext.Length == 1 && pipeline[businessNext[0]]!["recognition"] is null && pipeline[businessNext[0]]!["action"] is null)
                businessNext = Strings(pipeline[businessNext[0]]!["next"]!);
            Assert.Equal(new[] { "TT_CaptainHub", team < 5 ? $"TT_TryTeam{team + 1}" : "TT_AllTeamsExhausted" }, businessNext);
            Assert.Equal(new[] { click }, Strings(pipeline[confirm]!["next"]!));
            Assert.Null(pipeline[confirm]!["on_error"]);
            Assert.Equal(ExpectedRoi(team - 1), Numbers(pipeline[confirm]!["recognition"]!["param"]!["roi"]!));
        }
    }

    [Fact]
    public void 自定编队使用独立颜色范围并先验证后判断远征状态()
    {
        var pipeline = ReadPipeline("FormationConfig");
        Assert.Equal(new[] { "FC_ConfirmTeam", "FC_IsTeamOnExpedition", "FC_IsTeamEmpty", "FC_DissolveTeam" }, Strings(pipeline["FC_ClickTeam"]!["next"]!));
        var check = pipeline["FC_ConfirmTeam"]!;
        Assert.Equal(new[] { "FC_ClickTeam" }, Strings(check["next"]!));
        Assert.Null(check["on_error"]);
        Assert.Equal(new[] { 201, 106, 0 }, Numbers(check["recognition"]!["param"]!["lower"]!));
        Assert.Equal(new[] { 221, 126, 18 }, Numbers(check["recognition"]!["param"]!["upper"]!));
    }

    [Fact]
    public void 远征刷花的五个验证分支应返回刷花选队而非派遣流程()
    {
        var pipeline = ReadPipeline("Expedition");
        for (var team = 1; team <= 5; team++)
        {
            var check = pipeline[$"E_FlowerConfirmTeam{team}"]!;
            Assert.Equal(new[] { "E_FlowerTeamSelect" }, Strings(check["next"]!));
            Assert.Equal(new[] { "E_FlowerBattleHub" }, Strings(check["on_error"]!));
            Assert.Equal(new[] { "E_GoHome" }, Strings(pipeline[$"E_ConfirmTeamBtn{team}"]!["on_error"]!));
            Assert.Equal(ExpectedRoi(team - 1), Numbers(check["recognition"]!["param"]!["roi"]!));
        }
    }

    private static int[] ExpectedRoi(int index) => new[] { new[] { 89, 109, 1, 1 }, new[] { 210, 109, 1, 1 }, new[] { 332, 109, 1, 1 }, new[] { 453, 109, 1, 1 }, new[] { 576, 109, 1, 1 } }[index];
    private static int[] Numbers(JsonNode node) => node.AsArray().Select(value => value!.GetValue<int>()).ToArray();
    private static string[] Strings(JsonNode node) => node.AsArray().Select(value => value!.GetValue<string>()).ToArray();
    private static JsonNode ReadPipeline(string file) => JsonNode.Parse(File.ReadAllText(Path.Combine(Root(), "assets", "resource", "base", "pipeline", file + ".json")))!;
    private static JsonNode ReadInterface() => JsonNode.Parse(File.ReadAllText(Path.Combine(Root(), "assets", "interface.json")))!;

    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "assets", "interface.json"))) return directory.FullName;
        throw new DirectoryNotFoundException("找不到项目资源目录。");
    }
}
