using MFAAvalonia.ViewModels.UsersControls;
using MFAAvalonia.Extensions.MaaFW;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class FormationOptionsTests
{
    [Fact]
    public void 编队槽位内外行间距应保持一致()
    {
        var view = File.ReadAllText(FindFormationEditorViewPath());

        Assert.Contains("Margin=\"0,4,0,0\"", view, StringComparison.Ordinal);
        Assert.Contains("RowSpacing=\"4\"", view, StringComparison.Ordinal);
    }

    [Fact]
    public void 编队刀名应使用可搜索的原生下拉框()
    {
        var view = File.ReadAllText(FindFormationEditorViewPath());

        Assert.Contains("extensions:ComboBoxExtensions.CanSearch=\"True\"", view, StringComparison.Ordinal);
        Assert.Contains("extensions:ComboBoxExtensions.SearchWatermark=\"搜索刀剑名\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("AutoCompleteBox", view, StringComparison.Ordinal);
    }

    [Fact]
    public void 宝物选项应包含无和七个宝物名称()
    {
        Assert.Equal(
            ["无", "曜变天目", "狮子螺钿鞍", "南蛮胴具足", "锷・月下梅树透图", "锷・双鹤图", "三所物・菊", "三所物・狮子"],
            FormationOptions.TreasureOptions);
    }

    [Fact]
    public void 宝物无选项应转换为空值()
    {
        Assert.Equal("", FormationOptions.ToOcrTreasureName("无"));
    }

    [Theory]
    [InlineData("曜变天目", "变天目")]
    [InlineData("狮子螺钿鞍", "狮子螺")]
    [InlineData("南蛮胴具足", "南蛮")]
    [InlineData("锷・月下梅树透图", "月下")]
    [InlineData("锷・双鹤图", "双鹤")]
    [InlineData("三所物・菊", "三所物菊")]
    [InlineData("三所物・狮子", "三所物狮")]
    public void 宝物显示名称应映射到OCR短词(string displayName, string ocrName)
    {
        Assert.Equal(ocrName, FormationOptions.ToOcrTreasureName(displayName));
    }

    [Fact]
    public void 宝物页面确认应序列化为OnlyRecOCR()
    {
        var node = new MaaNode
        {
            Name = "FormationTreasurePageConfirm",
            Recognition = "OCR",
            Expected = ["宝物"],
            OnlyRec = true,
            Roi = new List<int> { 860, 96, 49, 25 },
        };

        var json = node.ToJson();

        Assert.Contains("\"only_rec\": true", json);
        Assert.Contains("\"expected\"", json);
        Assert.Contains("宝物", json);
    }

    private static string FindFormationEditorViewPath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var viewPath = Path.Combine(directory.FullName, "_src", "MFAAvalonia", "Views", "UserControls", "FormationEditorView.axaml");
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
                return viewPath;
        }

        throw new DirectoryNotFoundException("找不到 FormationEditorView.axaml 所在的仓库根目录。");
    }
}
