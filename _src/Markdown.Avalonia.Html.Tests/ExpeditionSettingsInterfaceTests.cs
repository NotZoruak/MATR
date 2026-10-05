using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class ExpeditionSettingsInterfaceTests
{
    [Fact]
    public void 本丸后勤应将远征设置收纳至远征开关()
    {
        var root = JsonNode.Parse(File.ReadAllText(FindInterfacePath()))!.AsObject();
        var logistics = root["task"]!.AsArray()
            .Single(item => item!["entry"]!.GetValue<string>() == "Expedition")!
            .AsObject();

        var topLevelOptions = logistics["option"]!.AsArray()
            .Select(item => item!.GetValue<string>())
            .ToArray();
        var globalOptions = root["global_option"]!.AsArray()
            .Select(item => item!.GetValue<string>())
            .ToArray();

        Assert.Equal("远征", topLevelOptions[0]);
        Assert.DoesNotContain("部队一", topLevelOptions);
        Assert.Contains("远征智能调度", globalOptions);
        Assert.DoesNotContain("长期远征计划", globalOptions);

        var smartScheduling = root["option"]!["远征智能调度"]!.AsObject();
        Assert.Equal("远征智能调度", smartScheduling["name"]!.GetValue<string>());
        Assert.Equal("后勤智能调度", smartScheduling["label"]!.GetValue<string>());

        var expedition = root["option"]!["远征"]!.AsObject();
        Assert.Equal("switch", expedition["type"]!.GetValue<string>());
        Assert.Equal("Yes", expedition["default_case"]!.GetValue<string>());
        Assert.Equal(
            ["部队一", "部队二", "部队三", "部队四", "部队五", "长期远征计划"],
            expedition["cases"]!.AsArray()[0]!["option"]!.AsArray()
                .Select(item => item!.GetValue<string>())
                .ToArray());
    }

    [Fact]
    public void 长期远征计划应以内联方式提供运行参数()
    {
        var root = JsonNode.Parse(File.ReadAllText(FindInterfacePath()))!.AsObject();
        var plan = root["option"]!["长期远征计划"]!.AsObject();

        Assert.True(plan["inline_sub_options"]!.GetValue<bool>());
        Assert.Equal("Yes", plan["default_case"]!.GetValue<string>());
        Assert.Equal(
            ["疲劳阈值", "临时部队记录槽"],
            plan["cases"]!.AsArray()[1]!["option"]!.AsArray()
                .Select(item => item!.GetValue<string>())
                .ToArray());

        var recordSlot = root["option"]!["临时部队记录槽"]!.AsObject();
        Assert.Equal("记录一", recordSlot["default_case"]!.GetValue<string>());
        Assert.Equal(
            ["记录一", "记录二", "记录三", "记录四", "记录五"],
            recordSlot["cases"]!.AsArray()
                .Select(item => item!["name"]!.GetValue<string>())
                .ToArray());
    }

    private static string FindInterfacePath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var interfacePath = Path.Combine(directory.FullName, "assets", "interface.json");
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
                return interfacePath;
        }

        throw new DirectoryNotFoundException("找不到 interface.json 所在的仓库根目录。");
    }
}
