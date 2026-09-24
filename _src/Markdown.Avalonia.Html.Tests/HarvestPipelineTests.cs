using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using MFAAvalonia.Extensions.MaaFW;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class HarvestPipelineTests
{
    [Fact]
    public void 收获物任务应使用纯标题设置分组类型()
    {
        var option = new MaaInterface.MaaInterfaceOption { Type = "setting" };

        Assert.True(option.IsSetting);
    }

    [Fact]
    public void 收获物任务应使用四个固定收获物检查区域并记录出阵打点()
    {
        var pipeline = JsonNode.Parse(File.ReadAllText(FindPipelinePath()))!.AsObject();
        var expectedRois = new[]
        {
            "[143,462,217,103]",
            "[441,461,218,104]",
            "[740,461,218,104]",
            "[1038,461,218,104]",
        };

        var checkNodes = pipeline
            .Where(pair => pair.Key.Contains("Region", StringComparison.Ordinal) && pair.Key.EndsWith("Check", StringComparison.Ordinal))
            .Select(pair => pair.Value!["recognition"]!["param"]!["roi"]!.ToJsonString())
            .ToArray();

        Assert.NotEmpty(checkNodes);
        Assert.All(checkNodes, roi => Assert.Contains(roi, expectedRois));
        Assert.DoesNotContain(pipeline, pair => pair.Value?["anchor"]?["HV_RoundDone"] is not null);
        Assert.Contains(
            pipeline,
            pair => pair.Value?["focus"]?["Node.Action.Succeeded"]?["content"]?.GetValue<string>() == "[刷收获物] 出阵");
        Assert.DoesNotContain(pipeline, pair => pair.Key.Contains("Isekai", StringComparison.Ordinal));
        Assert.DoesNotContain(
            pipeline,
            pair => pair.Value?["next"] is JsonValue);
    }

    [Fact]
    public void 收获物任务应使用独立HV选项且不包含任务级覆盖()
    {
        var interfaceRoot = JsonNode.Parse(File.ReadAllText(FindInterfacePath()))!.AsObject();
        var task = interfaceRoot["task"]!
            .AsArray()
            .Single(item => item!["name"]!.GetValue<string>() == "刷收获物")!
            .AsObject();

        Assert.Null(task["pipeline_override"]);
        var optionNames = task["option"]!.AsArray().Select(item => item!.GetValue<string>()).ToArray();
        Assert.Contains("HV_选择时代", optionNames);
        Assert.All(optionNames, name => Assert.StartsWith("HV_", name, StringComparison.Ordinal));

        var eraGroup = interfaceRoot["option"]!["HV_选择时代"]!.AsObject();
        Assert.Equal("setting", eraGroup["type"]!.GetValue<string>());
        var eraOptions = eraGroup["cases"]![0]!["option"]!.AsArray();
        Assert.Equal(8, eraOptions.Count);
        Assert.All(eraOptions, item => Assert.StartsWith("HV_时代", item!.GetValue<string>(), StringComparison.Ordinal));

        var outboundOptions = new[]
        {
            "HV_选择部队", "HV_选择阵形", "HV_重伤处理", "HV_疲劳处理", "HV_补充刀装",
            "HV_刀装保护", "HV_疲劳撤退", "HV_自动行军", "HV_换队长", "HV_不进王点",
            "HV_道中撤退", "HV_避战检非",
        };
        var definitions = interfaceRoot["option"]!.AsObject();
        Assert.All(outboundOptions, name => Assert.NotNull(definitions[name]));
        Assert.DoesNotContain(
            outboundOptions.SelectMany(name => definitions[name]!.ToJsonString().Split('"')),
            value => value.StartsWith("S_", StringComparison.Ordinal));
    }

    [Fact]
    public void 收获物时代确认后应先识别地域页面再验证时代()
    {
        var pipeline = JsonNode.Parse(File.ReadAllText(FindPipelinePath()))!.AsObject();

        for (var era = 1; era <= 8; era++)
        {
            var prefix = $"HV_Era{era}";
            var confirmNext = pipeline[$"{prefix}_Confirm"]!["next"]!.AsArray();
            var regionSelect = pipeline[$"{prefix}_IsRegionSelect"]!.AsObject();

            Assert.Equal(false, pipeline[$"{prefix}_Enter"]!["enabled"]?.GetValue<bool>());
            if (era == 5)
            {
                Assert.Contains("h", pipeline[$"{prefix}_Verify"]!["recognition"]!["param"]!["expected"]!.AsArray().Select(value => value!.GetValue<string>()));
            }
            Assert.Equal($"{prefix}_IsRegionSelect", confirmNext[0]!.GetValue<string>());
            Assert.Equal("[848,607,257,63]", regionSelect["recognition"]!["param"]!["roi"]!.ToJsonString());
            Assert.Equal($"{prefix}_Verify", regionSelect["next"]![0]!.GetValue<string>());

            Assert.Equal(500, pipeline[$"{prefix}_Enter"]!["post_delay"]!.GetValue<int>());
            Assert.Equal(200, pipeline[$"{prefix}_Confirm"]!["post_wait_freezes"]!["time"]!.GetValue<int>());
            Assert.Equal(
                "[1206,642,1,1]",
                pipeline[$"{prefix}_Confirm"]!["post_wait_freezes"]!["target"]!.ToJsonString());
            Assert.Equal(
                $"[刷收获物]时代{era}收获物已获取",
                pipeline[$"{prefix}_Done"]!["focus"]!["Node.Action.Succeeded"]!["content"]!.GetValue<string>());
            Assert.Equal(
                "log",
                pipeline[$"{prefix}_Done"]!["focus"]!["Node.Action.Succeeded"]!["display"]!.GetValue<string>());

            for (var region = 1; region <= 4; region++)
            {
                Assert.Equal(
                    "[0.9]",
                    pipeline[$"{prefix}_Region{region}_Check"]!["recognition"]!["param"]!["threshold"]!.ToJsonString());

                var regionCheck = pipeline[$"{prefix}_Region{region}_Check"]!.AsObject();
                var regionCheckNext = regionCheck["next"]!.AsArray();

                Assert.Contains(
                    regionCheckNext,
                    item => item!.GetValue<string>() == $"{prefix}_Verify");
                Assert.DoesNotContain(
                    regionCheckNext,
                    item => item!.GetValue<string>() == "HV_DetectWhereAmI");
                Assert.Equal(2, regionCheck["repeat"]!.GetValue<int>());
                Assert.Equal(500, regionCheck["repeat_delay"]!.GetValue<int>());
                Assert.Equal(
                    "[\"HV_DetectWhereAmI\"]",
                    regionCheck["on_error"]!.ToJsonString());
            }
        }

        Assert.DoesNotContain(
            pipeline["HV_DetectWhereAmI"]!["next"]!.AsArray(),
            item => item!.GetValue<string>() == "HV_IsPastRegionSelect");

        var regionSelectHub = pipeline["HV_IsRegionSelect"]!.AsObject();
        Assert.Contains(
            pipeline["HV_DetectWhereAmI"]!["next"]!.AsArray(),
            item => item!.GetValue<string>() == "HV_IsRegionSelect");
        Assert.Equal("[848,607,257,63]", regionSelectHub["recognition"]!["param"]!["roi"]!.ToJsonString());
        Assert.Equal("Common/地域选择.png", regionSelectHub["recognition"]!["param"]!["template"]!.GetValue<string>());
        Assert.Equal("[123,72,34,37]", regionSelectHub["action"]!["param"]!["target"]!.ToJsonString());
        Assert.Equal(200, regionSelectHub["post_wait_freezes"]!["time"]!.GetValue<int>());
        Assert.Equal("[123,72,34,37]", regionSelectHub["post_wait_freezes"]!["target"]!.ToJsonString());
        Assert.Equal("HV_DetectWhereAmI", regionSelectHub["next"]![0]!.GetValue<string>());
    }

    private static string FindPipelinePath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var pipelinePath = Path.Combine(directory.FullName, "assets", "resource", "base", "pipeline", "Harvest.json");
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
                return pipelinePath;
        }

        throw new DirectoryNotFoundException("找不到 Harvest.json 所在的仓库根目录。");
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
