using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Extensions.MaaFW.Custom;
using MFAAvalonia.Helper;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class GoalPtCheckRecognitionTests
{
    [Fact]
    public void 目标检查必须注册为识别而不是动作()
        => Assert.IsAssignableFrom<IMaaCustomRecognition>(new GoalPtCheckRecognition());

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("未识别")]
    [InlineData("-300000")]
    [InlineData("1/300000")]
    [InlineData("100 200")]
    [InlineData("1,23")]
    [InlineData("9999999999999999999999999999")]
    public void 无有效累计数字时不得命中目标(string? text)
    {
        var currentPt = Parse(text);
        Assert.Null(currentPt);
        Assert.False(ShouldSkip(currentPt, new(true, 1, false, null, null)));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("299999", 299999)]
    [InlineData("300,000", 300000)]
    [InlineData(" 300，000 ", 300000)]
    public void 应读取单个累计值并支持千位分隔符(string text, long expected)
        => Assert.Equal(expected, Parse(text));

    [Theory]
    [InlineData(299999, false)]
    [InlineData(300000, true)]
    [InlineData(300001, true)]
    public void 仅达到或超过目标时命中(long currentPt, bool expected)
        => Assert.Equal(expected, ShouldSkip(currentPt, new(true, 300000, false, null, null)));

    [Fact]
    public void 开关关闭或目标非法时不得命中()
    {
        Assert.False(ShouldSkip(300000, new(false, 300000, false, null, null)));
        Assert.False(ShouldSkip(300000, new(true, 0, false, null, null)));
        Assert.False(ShouldSkip(300000, new(true, 300000, true, null, null)));
    }

    [Fact]
    public void 日期计划应遵循五点边界并在开始前继续任务()
    {
        var settings = new ActivityGoalPtSettings(true, 100, true,
            new DateOnly(2026, 11, 12), new DateOnly(2026, 11, 13));
        Assert.False(ShouldSkip(100, settings, new DateTime(2026, 11, 12, 4, 59, 0)));
        Assert.True(ShouldSkip(50, settings, new DateTime(2026, 11, 12, 5, 0, 0)));
        Assert.False(ShouldSkip(50, settings, new DateTime(2026, 11, 13, 5, 0, 0)));
        Assert.True(ShouldSkip(100, settings, new DateTime(2026, 11, 14, 5, 0, 0)));
    }

    [Theory]
    [InlineData("Hanapai", "HP", "HP_SelectDifficulty")]
    [InlineData("RegimentBattle", "RB", "RB_IsRainbowBattle")]
    [InlineData("TacticalTraining", "TT", "TT_ClickDifficulty")]
    public void 达标在同一识别分支结束且未命中时继续原流程(string file, string prefix, string continuation)
    {
        var path = Path.Combine(FindRoot(), "assets", "resource", "base", "pipeline", file + ".json");
        var pipeline = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var check = pipeline[prefix + "_CheckGoalPt"];
        Assert.NotNull(check);
        Assert.False(check["enabled"]!.GetValue<bool>());
        Assert.Equal("Custom", check["recognition"]!["type"]!.GetValue<string>());
        Assert.Equal("GoalPtCheckRecognition", check["recognition"]!["param"]!["custom_recognition"]!.GetValue<string>());
        Assert.Equal("CompleteCurrentTaskAction", check["action"]!["custom_action"]!.GetValue<string>());
        Assert.Equal("目标 PT 已达成", check["action"]!["custom_action_param"]!["reason"]!.GetValue<string>());
        Assert.Null(check["next"]);
        Assert.Null(check["on_error"]);
        Assert.Null(pipeline[prefix + "_CompleteCurrentTaskAfterGoalPt"]);
        var candidates = pipeline[prefix + "_IsEventPage"]!["next"]!.AsArray()
            .Select(item => item!.GetValue<string>()).ToArray();
        Assert.Equal(prefix + "_CheckGoalPt", candidates[0]);
        Assert.Contains(continuation, candidates.Skip(1));
    }

    [Fact]
    public void 战术强化目标选项默认关闭并可启用检查和日期计划()
    {
        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(FindRoot(), "assets", "interface.json")))!;
        var task = config["task"]!.AsArray().Single(item =>
            item!["entry"]!.GetValue<string>() == "TacticalTraining")!;
        Assert.Equal("TT_目标PT", task["option"]![0]!.GetValue<string>());
        var options = config["option"]!;
        var goal = options["TT_目标PT"]!;
        Assert.Equal("No", goal["default_case"]!.GetValue<string>());
        var enabled = goal["cases"]!.AsArray().Single(item => item!["name"]!.GetValue<string>() == "Yes")!;
        Assert.True(enabled["pipeline_override"]!["TT_CheckGoalPt"]!["enabled"]!.GetValue<bool>());
        Assert.Equal(new[] { "TT_目标总PT", "TT_日期计划" },
            enabled["option"]!.AsArray().Select(item => item!.GetValue<string>()));
        var dates = options["TT_日期计划"]!["cases"]!.AsArray()
            .Single(item => item!["name"]!.GetValue<string>() == "Yes")!;
        Assert.All(dates["option"]!.AsArray(), name => Assert.NotNull(options[name!.GetValue<string>()]));
    }

    [Fact]
    public void 战术强化累计区域应使用用户提供的位置()
    {
        var pipeline = JsonNode.Parse(File.ReadAllText(Path.Combine(FindRoot(),
            "assets/resource/base/pipeline/TacticalTraining.json")))!;
        var roi = pipeline["TT_CheckGoalPt"]!["recognition"]!["param"]!["custom_recognition_param"]!["roi"]!;
        Assert.Equal(new[] { 845, 183, 108, 34 }, roi.AsArray().Select(item => item!.GetValue<int>()));
    }

    private static long? Parse(string? text) => GoalPtCheckRecognition.ParseCurrentPt(text);

    private static bool ShouldSkip(long? currentPt, ActivityGoalPtSettings settings, DateTime? now = null) =>
        GoalPtCheckRecognition.ShouldSkip(currentPt, settings, now ?? new DateTime(2026, 11, 12, 12, 0, 0));

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "assets", "interface.json")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir.FullName;
    }
}
