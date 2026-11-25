using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using MFAAvalonia.Extensions.MaaFW.Custom;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class DisassembleAllowListTests
{
    [Theory]
    [InlineData("short", 3, 0, true)]
    [InlineData("selected", 5, 3, false)]
    [InlineData("bottom", 5, 0, false)]
    public void 用户截图区分完整行选中状态和短列表(string name, int count, int selected, bool shortList)
    {
        var fixture = ReadFixture(name);
        var rows = ForgeDisassembleSelectAction.FindClearanceRows(ReadColumn(fixture, "edge"), ReadColumn(fixture, "selection"));
        Assert.Equal(count, rows.Count);
        Assert.Equal(selected, rows.Count(row => row.IsSelected));
        Assert.Equal(shortList, ForgeDisassembleSelectAction.IsShortClearanceList(ReadColumn(fixture, "edge"), ReadColumn(fixture, "selection")));
    }

    [Fact]
    public void 选中闪光不应漏掉相邻同名刀剑()
    {
        var during = ReadFixture("sparkle");
        var after = ReadFixture("after-sparkle");
        Assert.False(ReadColumn(during, "right").SequenceEqual(ReadColumn(after, "right")));
        var rows = ForgeDisassembleSelectAction.FindClearanceRows(ReadColumn(during, "edge"), ReadColumn(during, "selection"));
        Assert.Equal(5, rows.Count);
        Assert.Single(rows, row => row.IsSelected);
        Assert.Contains(rows, row => row.Top == 352 && !row.IsSelected);
    }

    [Fact]
    public void 空列表可结束但未知颜色不能当成空列表()
    {
        var blank = Enumerable.Range(0, 536).SelectMany(_ => new byte[] { 230, 244, 248, 255 }).ToArray();
        Assert.True(ForgeDisassembleSelectAction.IsShortClearanceList(blank, blank));
        Assert.False(ForgeDisassembleSelectAction.IsShortClearanceList(new byte[536 * 4], new byte[536 * 4]));
    }

    [Theory]
    [InlineData(80)]
    [InlineData(85)]
    [InlineData(89)]
    public void 不把列表底边截断的刀剑行当成完整行(int visibleHeight)
    {
        var column = new byte[536 * 4];
        for (var y = 536 - visibleHeight; y < 536; y++)
        {
            column[y * 4] = 162;
            column[y * 4 + 1] = 172;
            column[y * 4 + 2] = 175;
        }
        Assert.Empty(ForgeDisassembleSelectAction.FindClearanceRows(column, column));
        Assert.False(ForgeDisassembleSelectAction.IsShortClearanceList(column, column));
    }

    [Theory]
    [InlineData(3, 250, 260, "D_ClickDisassembleButton")]
    [InlineData(30, 260, 260, "D_ClickDisassembleButton")]
    [InlineData(0, 250, 260, "D_OpenMenuForMailbox")]
    [InlineData(0, 260, 260, "D_CompleteClearance")]
    public void 清仓先提交已选否则按刀位分流(int selected, int current, int maximum, string route)
        => Assert.Equal(route, ForgeDisassembleSelectAction.ResolveClearanceRoute(selected, current, maximum));

    [Theory]
    [InlineData("250 / 260", 250, 260)]
    [InlineData("所持刀剑250/260", 250, 260)]
    [InlineData("3/30", 3, 30)]
    public void 正确解析画面数量(string text, int current, int maximum)
    {
        Assert.True(ForgeDisassembleSelectAction.TryReadRatio(text, out var actual, out var capacity));
        Assert.Equal(current, actual);
        Assert.Equal(maximum, capacity);
    }

    [Theory]
    [InlineData("261/260")]
    [InlineData("0/0")]
    [InlineData("识别失败")]
    public void 非法数量不能参与容量判断(string text)
        => Assert.False(ForgeDisassembleSelectAction.TryReadRatio(text, out _, out _));

    [Fact]
    public void 开关只覆盖互斥入口且所有回路经过选刀入口()
    {
        var config = JsonNode.Parse(File.ReadAllText(RepositoryFile("assets/interface.json")))!;
        var option = config["option"]!["D_OnlyAllowedSwords"]!;
        Assert.Equal("switch", option["type"]!.GetValue<string>());
        Assert.Equal("No", option["default_case"]!.GetValue<string>());
        foreach (var item in option["cases"]!.AsArray())
        {
            var enabled = item!["name"]!.GetValue<string>() == "Yes";
            var overrides = item["pipeline_override"]!.AsObject();
            Assert.Equal(2, overrides.Count);
            Assert.Equal(enabled, overrides["D_SelectAllowedSwords"]!["enabled"]!.GetValue<bool>());
            Assert.Equal(!enabled, overrides["D_ClickDisassembleSelectAll"]!["enabled"]!.GetValue<bool>());
            Assert.All(overrides, pair => Assert.Single(pair.Value!.AsObject()));
        }
        var pipeline = JsonNode.Parse(File.ReadAllText(RepositoryFile("assets/resource/base/pipeline/Disassemble.json")))!.AsObject();
        Assert.NotNull(pipeline["D_SelectSwordsHub"]!["recognition"]);
        Assert.Equal("刀解选择", pipeline["D_SelectSwordsHub"]!["recognition"]!["param"]!["expected"]!.GetValue<string>());
        foreach (var pair in pipeline)
        {
            var next = pair.Value?["next"]?.AsArray().Select(item => item!.GetValue<string>()).ToArray() ?? [];
            if (pair.Key != "D_SelectSwordsHub")
                Assert.DoesNotContain("D_ClickDisassembleSelectAll", next);
        }
        Assert.False(pipeline["D_SelectAllowedSwords"]!["enabled"]!.GetValue<bool>());
        Assert.Equal("ForgeDisassembleSelectAction", pipeline["D_SelectAllowedSwords"]!["action"]!["custom_action"]!.GetValue<string>());
        Assert.Equal("clearance", pipeline["D_SelectAllowedSwords"]!["action"]!["custom_action_param"]!["mode"]!.GetValue<string>());
        Assert.Equal("CompleteCurrentTaskAction", pipeline["D_CompleteClearance"]!["action"]!["custom_action"]!.GetValue<string>());
        Assert.Null(pipeline["D_SelectAllowedSwords"]!["on_error"]);
    }

    private static JsonNode ReadFixture(string name)
    {
        var fixtures = JsonNode.Parse(File.ReadAllText(RepositoryFile("_src/Markdown.Avalonia.Html.Tests/Fixtures/DisassembleColumns.json")))!.AsArray();
        return fixtures.Single(item => item!["name"]!.GetValue<string>() == name)!;
    }

    private static byte[] ReadColumn(JsonNode fixture, string key)
    {
        var column = new List<byte>();
        foreach (var run in fixture[key]!.AsArray())
            for (var i = 0; i < run![0]!.GetValue<int>(); i++)
                column.AddRange([(byte)run[3]!.GetValue<int>(), (byte)run[2]!.GetValue<int>(), (byte)run[1]!.GetValue<int>(), 255]);
        return column.ToArray();
    }

    private static string RepositoryFile(string path)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
                return Path.Combine(directory.FullName, path);
        throw new DirectoryNotFoundException("找不到仓库目录");
    }
}
