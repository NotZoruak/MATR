using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class EquipSupplyPipelineTests
{
    [Fact]
    public void 确认部队记录后处理记录刀装不足弹窗()
    {
        var pipelinePath = FindPipelinePath();
        Assert.True(File.Exists(pipelinePath), "应提供 EquipSupply.json。\n" + pipelinePath);

        var pipeline = JsonNode.Parse(File.ReadAllText(pipelinePath))!.AsObject();
        var confirm = pipeline["ConfirmEquipmentSupply"]!.AsObject();
        var next = confirm["next"]!.AsArray()
            .Select(item => item!.GetValue<string>())
            .ToArray();

        Assert.Contains("ConfirmRecordShortageWithoutFallback", next);
        Assert.Contains("ConfirmRecordEquipmentShortage", next);
        Assert.True(
            Array.IndexOf(next, "ConfirmRecordEquipmentShortage")
            < Array.IndexOf(next, "ConfirmRecordShortageWithoutFallback"));

        var equipmentShortage = pipeline["ConfirmRecordEquipmentShortage"]!.AsObject();
        Assert.False(equipmentShortage["enabled"]!.GetValue<bool>());
        Assert.Equal("OCR", equipmentShortage["recognition"]!["type"]!.GetValue<string>());
        Assert.Equal(
            new[] { 568, 34, 142, 35 },
            equipmentShortage["recognition"]!["param"]!["roi"]!.AsArray()
                .Select(item => item!.GetValue<int>())
                .ToArray());
        Assert.Equal(
            "记录确认",
            equipmentShortage["recognition"]!["param"]!["expected"]!.GetValue<string>());
        Assert.Equal("Click", equipmentShortage["action"]!["type"]!.GetValue<string>());
        Assert.Equal(
            new[] { 1045, 524, 64, 43 },
            equipmentShortage["action"]!["param"]!["target"]!.AsArray()
                .Select(item => item!.GetValue<int>())
                .ToArray());
        Assert.Null(equipmentShortage["next"]);

        var shortage = pipeline["ConfirmRecordShortageWithoutFallback"]!.AsObject();
        Assert.Equal("OCR", shortage["recognition"]!["type"]!.GetValue<string>());
        Assert.Equal(
            new[] { 568, 34, 142, 35 },
            shortage["recognition"]!["param"]!["roi"]!.AsArray()
                .Select(item => item!.GetValue<int>())
                .ToArray());
        Assert.Equal(
            "记录确认",
            shortage["recognition"]!["param"]!["expected"]!.GetValue<string>());
        Assert.Equal("Click", shortage["action"]!["type"]!.GetValue<string>());
        Assert.Equal(
            new[] { 776, 526, 56, 41 },
            shortage["action"]!["param"]!["target"]!.AsArray()
                .Select(item => item!.GetValue<int>())
                .ToArray());
        Assert.Equal("LeaveTroopRecordSupply", shortage["next"]![0]!.GetValue<string>());

        var leave = pipeline["LeaveTroopRecordSupply"]!.AsObject();
        Assert.Equal("OCR", leave["recognition"]!["type"]!.GetValue<string>());
        Assert.Equal(
            new[] { 571, 4, 146, 40 },
            leave["recognition"]!["param"]!["roi"]!.AsArray()
                .Select(item => item!.GetValue<int>())
                .ToArray());
        Assert.Equal("部队记录", leave["recognition"]!["param"]!["expected"]!.GetValue<string>());
        Assert.Equal("Click", leave["action"]!["type"]!.GetValue<string>());
        Assert.Equal(
            new[] { 57, 19, 31, 30 },
            leave["action"]!["param"]!["target"]!.AsArray()
                .Select(item => item!.GetValue<int>())
                .ToArray());
        Assert.Equal("LogRecordShortage", leave["next"]![0]!.GetValue<string>());

        var log = pipeline["LogRecordShortage"]!.AsObject();
        Assert.Equal("OCR", log["recognition"]!["type"]!.GetValue<string>());
        Assert.Equal(
            new[] { 571, 4, 146, 40 },
            log["recognition"]!["param"]!["roi"]!.AsArray()
                .Select(item => item!.GetValue<int>())
                .ToArray());
        Assert.Equal("部队选择", log["recognition"]!["param"]!["expected"]!.GetValue<string>());
        Assert.Null(log["next"]);
        Assert.Equal("Custom", log["action"]!["type"]!.GetValue<string>());
        Assert.Equal(
            "CompleteCurrentTaskAction",
            log["action"]!["custom_action"]!.GetValue<string>());
        Assert.Equal(
            "刀装不足",
            log["action"]!["custom_action_param"]!["reason"]!.GetValue<string>());
    }

    private static string FindPipelinePath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var pipelinePath = Path.Combine(directory.FullName, "assets", "resource", "base", "pipeline", "EquipSupply.json");
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
                return pipelinePath;
        }

        throw new DirectoryNotFoundException("找不到 EquipSupply.json 所在的仓库根目录。");
    }
}
