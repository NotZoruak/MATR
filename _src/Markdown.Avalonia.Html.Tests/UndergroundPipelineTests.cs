using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class UndergroundPipelineTests
{
    [Fact]
    public void 地下城主枢纽挂载公共修刀刀装与刷花出口锚点()
    {
        var pipelinePath = FindPipelinePath();
        Assert.True(File.Exists(pipelinePath), "应提供 Underground.json。\n" + pipelinePath);

        var pipeline = JsonNode.Parse(File.ReadAllText(pipelinePath))!.AsObject();
        var hub = pipeline["U_DetectWhereAmI"]!.AsObject();
        var anchors = hub["anchor"]!.AsObject();

        Assert.Equal("U_DetectWhereAmI", anchors["Hub"]!.GetValue<string>());
        Assert.Equal("U_DetectWhereAmI", anchors["RepairDone"]!.GetValue<string>());
        Assert.Equal("U_DetectWhereAmI", anchors["RepairAborted"]!.GetValue<string>());
        Assert.Equal("U_IsPreSortieConfirm", anchors["SupplyDone"]!.GetValue<string>());
        Assert.Equal("U_DetectWhereAmI", anchors["GrindDone"]!.GetValue<string>());

        var postSortieNext = pipeline["U_PostSortieHub"]!["next"]!.AsArray()
            .Select(item => item!.GetValue<string>())
            .ToArray();
        Assert.Contains("IsPreDamage", postSortieNext);
        Assert.Contains("U_CheckEquipmentPopup", postSortieNext);
        Assert.Equal("IsEquipmentShortagePopup", pipeline["U_CheckEquipmentPopup"]!["next"]![0]!.GetValue<string>());
        Assert.Equal("TF_EnterFromTeamSelect", pipeline["U_FatigueCheck"]!["on_error"]![0]!.GetValue<string>());
    }

    [Fact]
    public void 地下城重伤停止使用日志与系统通知焦点()
    {
        var pipelinePath = FindPipelinePath();
        var pipeline = JsonNode.Parse(File.ReadAllText(pipelinePath))!.AsObject();
        var node = pipeline["U_LogStopOnDamage"]!.AsObject();

        Assert.Null(node["action"]);
        var focus = node["focus"]!["Node.Action.Succeeded"]!.AsObject();
        Assert.Equal("special:[重伤检测] 检测到刀剑男士重伤，任务终止", focus["content"]!.GetValue<string>());
        var display = focus["display"]!.AsArray().Select(item => item!.GetValue<string>()).ToArray();
        Assert.Contains("log", display);
        Assert.Contains("notification", display);
        Assert.Null(node["on_error"]);
    }

    private static string FindPipelinePath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var pipelinePath = Path.Combine(directory.FullName, "assets", "resource", "base", "pipeline", "Underground.json");
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
                return pipelinePath;
        }

        throw new DirectoryNotFoundException("找不到 Underground.json 所在的仓库根目录。");
    }
}
