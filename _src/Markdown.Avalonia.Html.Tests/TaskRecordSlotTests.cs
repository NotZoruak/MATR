using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper.ValueType;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class TaskRecordSlotTests
{
    private static readonly string[] Prefixes = ["S", "U", "RB", "HP", "EC", "TT", "HV"];
    private static readonly string[] RecordNodes = ["SelectRecordSlot", "TF_UseRecord_Step2", "THR_UseRecord_Step2"];
    private static readonly Dictionary<string, string> TeamNodes = new()
    {
        ["S"] = "S_IsTeamSelect",
        ["U"] = "U_ClickTeam",
        ["RB"] = "RB_IsTeamSelect",
        ["HP"] = "HP_IsTeamSelect",
        ["EC"] = "EC_IsTeamSelect",
        ["TT"] = "TT_ClickTargetTeam",
        ["HV"] = "HV_IsTeamSelect",
    };

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void 旧任务缺少记录选项时沿用原部队编号(int teamIndex)
    {
        foreach (var prefix in Prefixes)
        {
            var task = SavedTask(prefix, teamIndex);
            Synchronize(task, Definition(prefix));
            Assert.Equal(teamIndex, task.Option!.Single(option => option.Name == prefix + "_部队记录").Index);
        }
    }

    [Fact]
    public void 已保存记录槽不随改选部队或重复加载改变()
    {
        var task = SavedTask("S", 1);
        Synchronize(task, Definition("S"));
        task.Option!.Single(option => option.Name == "S_部队记录").Index = 4;
        task.Option!.Single(option => option.Name == "S_选择部队").Index = 3;
        var reloaded = JsonConvert.DeserializeObject<MaaInterface.MaaInterfaceTask>(JsonConvert.SerializeObject(task))!;

        Synchronize(reloaded, Definition("S"));
        Synchronize(reloaded, Definition("S"));

        Assert.Equal(3, reloaded.Option!.Single(option => option.Name == "S_选择部队").Index);
        Assert.Equal(4, reloaded.Option!.Single(option => option.Name == "S_部队记录").Index);
        Assert.Equal(2, reloaded.Option!.Count);
    }

    [Fact]
    public void 不同任务对象分别迁移且新任务默认记录一()
    {
        var first = SavedTask("U", 1);
        var second = SavedTask("U", 3);
        var fresh = new MaaInterface.MaaInterfaceTask { Option = [] };
        Synchronize(first, Definition("U"));
        Synchronize(second, Definition("U"));
        Synchronize(fresh, Definition("U"));
        first.Option!.Single(option => option.Name == "U_部队记录").Index = 4;

        Assert.Equal(3, second.Option!.Single(option => option.Name == "U_部队记录").Index);
        Assert.Equal(0, fresh.Option!.Single(option => option.Name == "U_部队记录").Index);
    }

    [Fact]
    public void 每个任务的二十五种部队记录组合互不干扰()
    {
        var root = ReadInterface();
        foreach (var prefix in Prefixes)
        {
            var task = root["task"]!.Single(task => task["option"]!.Values<string>().Contains(prefix + "_选择部队"));
            Assert.Contains(prefix + "_部队记录", task["option"]!.Values<string>());
            var teamCases = root["option"]![prefix + "_选择部队"]!["cases"]!;
            var recordOption = root["option"]![prefix + "_部队记录"];
            Assert.NotNull(recordOption);
            Assert.Equal(5, recordOption!["cases"]!.Count());

            for (var teamIndex = 0; teamIndex < 5; teamIndex++)
            {
                var teamOverrides = (JObject)teamCases[teamIndex]!["pipeline_override"]!;
                Assert.All(RecordNodes, name => Assert.Null(teamOverrides[name]));
                // 部队选项只负责指定操作部队，不负责指定恢复编队使用的记录槽。
                var teamNode = TeamNodes[prefix];
                Assert.NotNull(teamOverrides[teamNode]?["target"]);

                for (var recordIndex = 0; recordIndex < 5; recordIndex++)
                {
                    var recordOverrides = (JObject)recordOption["cases"]![recordIndex]!["pipeline_override"]!;
                    Assert.Equal(RecordNodes.Order(), recordOverrides.Properties().Select(property => property.Name).Order());
                    var merged = (JObject)teamOverrides.DeepClone();
                    merged.Merge(recordOverrides);
                    var expected = new[] { 1209, new[] { 140, 250, 360, 465, 575 }[recordIndex], 30, 71 };
                    foreach (var name in RecordNodes)
                        Assert.Equal(expected, merged[name]!["target"]!.Values<int>());
                    Assert.Equal(teamOverrides[teamNode]!.ToString(), merged[teamNode]!.ToString());
                }
            }
        }
    }

    [Fact]
    public void 独立刷花远征与战术强化多记录保留各自用途()
    {
        var root = ReadInterface();
        Assert.Null(root["option"]!["FB_部队记录"]);
        Assert.Equal(5, root["option"]!["临时部队记录槽"]!["cases"]!.Count());
        var recordCases = root["option"]!["TT_启用部队记录"]!["cases"]!;
        for (var index = 0; index < 5; index++)
        {
            var overrides = (JObject)recordCases[index]!["pipeline_override"]!;
            Assert.Equal("TT_TryRecord" + (index + 1), Assert.Single(overrides.Properties()).Name);
        }
    }

    private static MaaInterface.MaaInterfaceTask SavedTask(string prefix, int teamIndex) => new()
    {
        Option = [new() { Name = prefix + "_选择部队", Index = teamIndex }]
    };

    private static MaaInterface Definition(string prefix) => new()
    {
        Option = new Dictionary<string, MaaInterface.MaaInterfaceOption>
        {
            [prefix + "_选择部队"] = new()
            {
                DefaultCases = ["部队一"],
                Cases = Enumerable.Range(1, 5).Select(index => new MaaInterface.MaaInterfaceOptionCase { Name = "部队" + (index == 1 ? "一" : index.ToString()) }).ToList()
            },
            [prefix + "_部队记录"] = new()
            {
                DefaultCases = ["记录一"],
                Cases = Enumerable.Range(1, 5).Select(index => new MaaInterface.MaaInterfaceOptionCase { Name = "记录" + (index == 1 ? "一" : index.ToString()) }).ToList()
            }
        }
    };

    private static void Synchronize(MaaInterface.MaaInterfaceTask task, MaaInterface definition)
    {
        var loader = new TaskLoader(definition, null!);
        var current = new MaaInterface.MaaInterfaceTask
        {
            Option = definition.Option!.Keys.Select(name => new MaaInterface.MaaInterfaceSelectOption { Name = name }).ToList()
        };
        var drag = (DragItemViewModel)RuntimeHelpers.GetUninitializedObject(typeof(DragItemViewModel));
        typeof(DragItemViewModel)
            .GetField("_interfaceItem", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(drag, task);
        typeof(TaskLoader).GetMethod("UpdateOptions", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(loader, [drag, current]);
    }

    private static JObject ReadInterface()
    {
        foreach (var start in new[]
                 {
                     Path.GetDirectoryName(typeof(TaskLoader).Assembly.Location)!,
                     AppContext.BaseDirectory,
                     Directory.GetCurrentDirectory(),
                 })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var path = Path.Combine(directory.FullName, "assets", "interface.json");
                if (File.Exists(path)) return JObject.Parse(File.ReadAllText(path));
            }
        }
        throw new DirectoryNotFoundException("找不到项目资源目录。");
    }
}
