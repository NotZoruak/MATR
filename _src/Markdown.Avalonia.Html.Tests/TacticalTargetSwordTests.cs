using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Extensions.MaaFW.Custom;
using MFAAvalonia.Helper;
using MFAAvalonia.Models;
using MFAAvalonia.Services;
using MFAAvalonia.ViewModels.UsersControls.Settings;
using SkiaSharp;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class TacticalTargetSwordTests
{
    private static bool Refresh(RecognitionResult[] results, int[] targets) =>
        TacticalTargetSwordRecognition.ShouldRefresh(results, targets);

    private static string[] Names(MaaInterface.MaaInterfaceSelectOption option) =>
        TacticalTargetSwordSettings.ReadNames(option).ToArray();

    private static RecognitionResult Sword(int id, double score, bool head = true) =>
        new() { Id = id, Name = id.ToString(), Score = score, IsHead = head };

    [Theory]
    [InlineData(0.90, 0.89, true)]
    [InlineData(0.91, 0.90, true)]
    [InlineData(0.91, 0.9001, false)]
    [InlineData(0.8999, 0.80, true)]
    [InlineData(0.85, 0.80, true)]
    [InlineData(0.85, 0.8001, false)]
    [InlineData(0.8499, 0.70, false)]
    [InlineData(0.8999, 0.85, false)]
    [InlineData(0.99, 0.98, true)]
    public void 达到可信度和最小差距阈值才刷新(double first, double second, bool expected)
        => Assert.Equal(expected, Refresh([Sword(1, first), Sword(2, second)], [3]));

    [Fact]
    public void 上下半身应按刀剑合并后比较不同刀剑()
        => Assert.True(Refresh([Sword(1, 0.99), Sword(1, 0.98, false), Sword(2, 0.90)], [3]));

    [Fact]
    public void 目标刀剑或空名单不刷新()
    {
        Assert.False(Refresh([Sword(1, 0.99), Sword(2, 0.80)], [1]));
        Assert.False(Refresh([Sword(1, 0.99), Sword(2, 0.80)], []));
    }

    [Theory]
    [InlineData(true, "目标刀剑，继续出阵")]
    [InlineData(false, "非目标刀剑，刷新剪影")]
    public void 可靠结果无论是否目标都应产生日志(bool isTarget, string expected)
    {
        var results = new[]
        {
            new RecognitionResult { Id = 1, Name = "三日月宗近", Score = 0.99 },
            Sword(2, 0.80)
        };
        var decision = TacticalTargetSwordRecognition.Evaluate(results, isTarget ? new[] { 1 } : new[] { 3 });
        Assert.Equal(!isTarget, decision.Refresh);
        Assert.Contains("三日月宗近", decision.Message);
        Assert.Contains(expected, decision.Message);
    }

    [Fact]
    public void 不可靠结果不应输出确认刀剑的日志()
    {
        var decision = TacticalTargetSwordRecognition.Evaluate([Sword(1, 0.8999), Sword(2, 0.85)], [1]);
        Assert.Null(decision.Message);
        Assert.False(decision.Refresh);
    }

    [Fact]
    public void 不足两个候选或分数非法不刷新()
    {
        Assert.False(Refresh([], [3]));
        Assert.False(Refresh([Sword(1, 0.99)], [3]));
        Assert.False(Refresh([Sword(1, double.NaN), Sword(2, 0.8)], [3]));
        Assert.False(Refresh([Sword(1, 1.01), Sword(2, 0.8)], [3]));
    }

    [Fact]
    public void 首次缺省使用指定名单但主动清空不恢复()
    {
        var defaults = Names(new());
        Assert.Equal(24, defaults.Length);
        Assert.Contains("三日月宗近", defaults);
        Assert.Contains("泛尘", defaults);
        Assert.Empty(Names(new() { Data = new() { ["swords"] = "[]" } }));
        Assert.Empty(Names(new() { Data = new() { ["swords"] = "无效配置" } }));
    }

    [Fact]
    public void 两个任务的目标名单互不影响()
    {
        var first = new MaaInterface.MaaInterfaceSelectOption { Data = new() { ["swords"] = "[\"小狐丸\"]" } };
        var second = new MaaInterface.MaaInterfaceSelectOption { Data = new() { ["swords"] = "[\"泛尘\"]" } };
        Assert.Equal(new[] { "小狐丸" }, Names(first));
        Assert.Equal(new[] { "泛尘" }, Names(second));
    }

    [Fact]
    public void 设置页搜索增删应即时保存当前任务并保留空名单()
    {
        var option = new MaaInterface.MaaInterfaceSelectOption { Data = new() { ["swords"] = "[]" } };
        var saves = 0;
        var catalog = new[] { new SwordCatalogEntry("小狐丸", "太刀", "小狐丸"), new SwordCatalogEntry("泛尘", "胁差", "泛尘") };
        var model = new TacticalTargetSwordUserControlModel(option, () => saves++, catalog);
        model.SearchText = "太刀";
        Assert.Equal("小狐丸", Assert.Single(model.Candidates).BaseName);
        model.AddCommand.Execute("小狐丸");
        Assert.Equal(1, saves);
        Assert.True(Assert.Single(model.Candidates).IsSelected);
        Assert.Equal(new[] { "小狐丸" }, Names(option));
        Assert.Single(new TacticalTargetSwordUserControlModel(option, () => { }, catalog).Tags);
        model.RemoveCommand.Execute("小狐丸");
        Assert.Equal(2, saves);
        Assert.Empty(Names(option));
        Assert.Empty(new TacticalTargetSwordUserControlModel(option, () => { }, catalog).Tags);
    }

    [Fact]
    public void 自动筛选排名应保留低分第二名且不改变工具结果()
    {
        var directory = Path.Combine(Path.GetTempPath(), "matr-silhouette-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var template = new SKBitmap(300, 300);
            using var paint = new SKPaint { Color = SKColors.Black };
            using (var canvas = new SKCanvas(template)) canvas.Clear(SKColors.Black);
            SaveTemplate("00001_甲_head.png");
            SaveTemplate("00001_甲_foot.png");
            using (var canvas = new SKCanvas(template))
            {
                canvas.Clear(SKColors.White);
                for (var column = 0; column < 3; column++)
                    canvas.DrawRect(column * 100, 0, 25, 300, paint);
            }
            SaveTemplate("00002_乙_head.png");
            using var shot = new SKBitmap(1280, 720);
            using (var canvas = new SKCanvas(shot))
            {
                canvas.Clear(SKColors.White);
                canvas.DrawRect(708, 233, 337, 337, paint);
            }
            var service = new SilhouetteService();
            service.LoadTemplates(directory);
            var ranked = service.RecognizeSwords(shot);
            Assert.Equal(2, ranked.Count);
            Assert.Equal(1, ranked[0].Id);
            Assert.Equal(2, ranked[1].Id);
            Assert.True(ranked[0].Score > 0.95);
            Assert.True(ranked[1].Score < 0.55);
            Assert.All(service.Recognize(shot), item => Assert.Equal(1, item.Id));

            void SaveTemplate(string name)
            {
                using var data = template.Encode(SKEncodedImageFormat.Png, 100);
                using var output = File.Create(Path.Combine(directory, name));
                data.SaveTo(output);
            }
        }
        finally
        {
            foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public void 筛选应处于第二位并使用独立刷新分支()
    {
        var root = FindRoot();
        var pipeline = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "assets/resource/base/pipeline/TacticalTraining.json")))!;
        var next = pipeline["TT_VerifyEventPage"]!["next"]!.AsArray();
        Assert.Equal(new[] { "TT_CheckSilhouette", "TT_CheckNonTargetSword", "TT_EnterEvent" },
            next.Select(item => item!.GetValue<string>()));
        var check = pipeline["TT_CheckNonTargetSword"]!;
        Assert.NotNull(check);
        Assert.False(check["enabled"]!.GetValue<bool>());
        Assert.Equal(1, check["max_hit"]!.GetValue<int>());
        Assert.Equal("TacticalTargetSwordRecognition", check["recognition"]!["param"]!["custom_recognition"]!.GetValue<string>());
        Assert.Equal("Click", check["action"]!["type"]!.GetValue<string>());
        Assert.Equal(new[] { "TT_VerifyNonTargetSilhouettePopup", "TT_ClickNonTargetSilhouetteRefresh" },
            check["next"]!.AsArray().Select(item => item!.GetValue<string>()));

        var popup = pipeline["TT_VerifyNonTargetSilhouettePopup"]!;
        Assert.True(JsonNode.DeepEquals(pipeline["TT_IsSilhouettePopup"]!["recognition"], popup["recognition"]));
        Assert.Null(popup["action"]);
        Assert.Null(popup["next"]);
        Assert.Null(popup["on_error"]);
        var refresh = pipeline["TT_ClickNonTargetSilhouetteRefresh"]!;
        Assert.Equal("OCR", refresh["recognition"]!["type"]!.GetValue<string>());
        Assert.Equal(new[] { 1098, 249, 121, 31 },
            refresh["recognition"]!["param"]!["roi"]!.AsArray().Select(item => item!.GetValue<int>()));
        Assert.Equal("剪影更新", refresh["recognition"]!["param"]!["expected"]!.GetValue<string>());
        Assert.Equal("Click", refresh["action"]!["type"]!.GetValue<string>());
        Assert.Equal(new[] { "TT_VerifyNonTargetSilhouettePopup", "TT_ClickNonTargetSilhouetteRefresh" },
            refresh["next"]!.AsArray().Select(item => item!.GetValue<string>()));
        Assert.Equal("TT_DetectWhereAmI", refresh["on_error"]![0]!.GetValue<string>());

        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "assets", "interface.json")))!;
        var task = config["task"]!.AsArray().Single(item => item!["entry"]!.GetValue<string>() == "TacticalTraining")!;
        Assert.Equal(new[] { "TT_目标PT", "TT_筛选目标刀剑" },
            task["option"]!.AsArray().Take(2).Select(item => item!.GetValue<string>()));
        var options = config["option"]!;
        Assert.Equal("No", options["TT_筛选目标刀剑"]!["default_case"]!.GetValue<string>());
        var enabled = options["TT_筛选目标刀剑"]!["cases"]![1]!;
        Assert.True(enabled["pipeline_override"]!["TT_CheckNonTargetSword"]!["enabled"]!.GetValue<bool>());
        Assert.Equal("TT_目标刀剑", enabled["option"]![0]!.GetValue<string>());
        var saved = options["TT_目标刀剑"]!["inputs"]![0]!["default"]!.GetValue<string>();
        Assert.Equal(TacticalTargetSwordSettings.DefaultNames,
            Names(new() { Data = new() { ["swords"] = saved } }));
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "assets", "interface.json")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory.FullName;
    }

    [Fact]
    public void 选择难度后应优先识别并双击巧克力苦无战斗训练()
    {
        var path = Path.Combine(FindRoot(), "assets", "resource", "base", "pipeline", "TacticalTraining.json");
        var pipeline = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.Equal("TT_ClickChocolateKunaiTraining", pipeline["TT_ClickDifficulty"]!["next"]![0]!.GetValue<string>());
        var node = pipeline["TT_ClickChocolateKunaiTraining"]!;
        Assert.Equal("TemplateMatch", node["recognition"]!["type"]!.GetValue<string>());
        var param = node["recognition"]!["param"]!;
        Assert.Equal(new[] { 705, 235, 339, 343 }, param["roi"]!.AsArray().Select(item => item!.GetValue<int>()));
        var templates = param["template"] is JsonArray array
            ? array.Select(item => item!.GetValue<string>()).ToArray()
            : new[] { param["template"]!.GetValue<string>() };
        Assert.NotEmpty(templates);
        Assert.All(templates, template =>
        {
            Assert.StartsWith("Activity/巧克力_苦无战斗训练", template);
            Assert.True(File.Exists(Path.Combine(FindRoot(), "assets", "resource", "base", "image", template)));
        });
        Assert.Equal("Click", node["action"]!["type"]!.GetValue<string>());
        Assert.Equal(2, node["repeat"]!.GetValue<int>());
        Assert.Equal(new[] { "TT_EnterEvent", "TT_DetectWhereAmI" }, node["next"]!.AsArray().Select(item => item!.GetValue<string>()));
        Assert.Null(node["on_error"]);
    }

    [Fact]
    public void 出阵打点应在成功识别后记录而非点击按钮时记录()
    {
        var root = Path.Combine(FindRoot(), "assets", "resource", "base", "pipeline");
        var pipeline = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "TacticalTraining.json")))!;
        var sortie = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "Sortie.json")))!;
        var confirm = pipeline["TT_IsPreSortieConfirm"]!;
        Assert.Null(confirm["focus"]);
        Assert.Equal(new[] { "IsPreDamage", "IsEquipmentShortagePopup", "TT_IsPreSortieConfirm", "TT_SortieSuccess" },
            confirm["next"]!.AsArray().Select(item => item!.GetValue<string>()));
        var success = pipeline["TT_SortieSuccess"]!;
        Assert.NotNull(success);
        Assert.True(JsonNode.DeepEquals(sortie["S_SortieSuccess"]!["recognition"], success["recognition"]));
        Assert.Equal("TT_DetectWhereAmI", success["next"]![0]!.GetValue<string>());
        Assert.Equal("[战术强化] 出阵", success["focus"]!["Node.Action.Succeeded"]!["content"]!.GetValue<string>());
        Assert.Equal("file", success["focus"]!["Node.Action.Succeeded"]!["display"]!.GetValue<string>());
        Assert.Null(success["on_error"]);
    }
}
