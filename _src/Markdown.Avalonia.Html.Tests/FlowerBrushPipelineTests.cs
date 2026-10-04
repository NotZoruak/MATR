using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class FlowerBrushPipelineTests
{
    [Fact]
    public void 选刀后应先确认返回部队选择页面()
    {
        var pipeline = JsonNode.Parse(File.ReadAllText(FindRepositoryFile("assets", "resource", "base", "pipeline", "FlowerBrush.json")))!.AsObject();
        var next = pipeline["FB_IsSwordSelect"]!["next"]!.AsArray();

        Assert.Equal("FB_IsTeamSelectAfterSwordSelection", next[0]!.GetValue<string>());
        Assert.Equal("OCR", pipeline["FB_IsTeamSelectAfterSwordSelection"]!["recognition"]!["type"]!.GetValue<string>());
        Assert.Equal("部队选择", pipeline["FB_IsTeamSelectAfterSwordSelection"]!["recognition"]!["param"]!["expected"]!.GetValue<string>());
        Assert.Equal(new[] { 561, 1, 160, 49 }, pipeline["FB_IsTeamSelectAfterSwordSelection"]!["recognition"]!["param"]!["roi"]!.AsArray().Select(item => item!.GetValue<int>()));
        Assert.Equal("FB_ClickSortieNow", pipeline["FB_IsTeamSelectAfterSwordSelection"]!["next"]![1]!.GetValue<string>());
    }

    [Fact]
    public void 新队长满疲劳时应结束刷花任务()
    {
        var pipeline = JsonNode.Parse(File.ReadAllText(FindRepositoryFile("assets", "resource", "base", "pipeline", "FlowerBrush.json")))!.AsObject();
        var selectionNext = pipeline["FB_IsTeamSelectAfterSwordSelection"]!["next"]!.AsArray();
        var completionNode = pipeline["FB_CompleteCurrentTaskAfterAllEligibleSwordsRecovered"]!;
        var recognition = completionNode["recognition"]!;

        Assert.Equal("FB_CompleteCurrentTaskAfterAllEligibleSwordsRecovered", selectionNext[0]!.GetValue<string>());
        Assert.Equal("OCR", recognition["type"]!.GetValue<string>());
        Assert.Equal(new[] { 341, 191, 33, 18 }, recognition["param"]!["roi"]!.AsArray().Select(item => item!.GetValue<int>()));
        Assert.Equal(new[] { "100" }, recognition["param"]!["expected"]!.AsArray().Select(item => item!.GetValue<string>()));
        Assert.True(recognition["param"]!["only_rec"]!.GetValue<bool>());
        Assert.Equal("CompleteCurrentTaskAction", completionNode["action"]!["custom_action"]!.GetValue<string>());
        Assert.Equal("符合条件的刀剑疲劳值均已恢复完毕", completionNode["action"]!["custom_action_param"]!["reason"]!.GetValue<string>());
    }

    [Fact]
    public void 队长满疲劳后应解除装备并返回替换队长流程()
    {
        var pipeline = JsonNode.Parse(File.ReadAllText(FindRepositoryFile("assets", "resource", "base", "pipeline", "FlowerBrush.json")))!.AsObject();

        Assert.Equal("FB_ClickEquipmentSlotForUnequip", pipeline["FB_CheckCaptainFatigue"]!["next"]![0]!.GetValue<string>());
        Assert.Equal("FB_ConfirmAutoUnequip", pipeline["FB_ClickEquipmentSlotForUnequip"]!["next"]![0]!.GetValue<string>());
        Assert.Equal("一键卸装", pipeline["FB_ConfirmAutoUnequip"]!["recognition"]!["param"]!["expected"]!.GetValue<string>());
        Assert.Equal(new[] { 702, 81, 96, 27 }, pipeline["FB_ConfirmAutoUnequip"]!["recognition"]!["param"]!["roi"]!.AsArray().Select(item => item!.GetValue<int>()));
        Assert.DoesNotContain("FB_ConfirmAutoUnequip", pipeline["FB_ClickEquipmentSlot"]!["next"]!.AsArray().Select(item => item!.GetValue<string>()));
        var leaveNext = pipeline["FB_LeaveEquipmentPageAfterUnequip"]!["next"]!.AsArray();
        Assert.Equal("FB_IsTeamSelectAfterUnequip", leaveNext[0]!.GetValue<string>());
        Assert.Equal("FB_LeaveEquipmentPageAfterUnequip", leaveNext[1]!.GetValue<string>());
        Assert.Equal("OCR", pipeline["FB_IsTeamSelectAfterUnequip"]!["recognition"]!["type"]!.GetValue<string>());
        Assert.Equal("部队选择", pipeline["FB_IsTeamSelectAfterUnequip"]!["recognition"]!["param"]!["expected"]!.GetValue<string>());
        Assert.Equal(new[] { 561, 1, 160, 49 }, pipeline["FB_IsTeamSelectAfterUnequip"]!["recognition"]!["param"]!["roi"]!.AsArray().Select(item => item!.GetValue<int>()));
        Assert.Null(pipeline["FB_IsTeamSelectAfterUnequip"]!["next"]);
    }

    private static string FindRepositoryFile(params string[] pathParts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine([directory.FullName, .. pathParts]);
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
                return path;
        }

        throw new DirectoryNotFoundException($"找不到仓库文件：{Path.Combine(pathParts)}");
    }
}
