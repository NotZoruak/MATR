using MFAAvalonia.Services;
using System.Linq;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class SwordCatalogSearchTests
{
    [Theory]
    [InlineData("太", "短刀", "太郎太刀", true)]
    [InlineData("太", "太刀", "小狐丸", true)]
    [InlineData("太", "大太刀", "石切丸", true)]
    [InlineData("太刀", "太刀", "小狐丸", true)]
    [InlineData("太刀", "大太刀", "石切丸", true)]
    [InlineData("太刀", "打刀", "へし切長谷部", false)]
    [InlineData("差", "胁差", "骨喰藤四郎", true)]
    [InlineData("差", "短刀", "药研藤四郎", false)]
    [InlineData("清光", "打刀", "加州清光", true)]
    public void Matches_同时按名称和刀种包含关系筛选(
        string keyword,
        string type,
        string name,
        bool expected)
    {
        Assert.Equal(expected, SwordCatalogService.Matches(name, type, keyword));
    }

    [Fact]
    public void Matches_空搜索不命中候选()
    {
        Assert.False(SwordCatalogService.Matches("加州清光", "打刀", ""));
    }

    [Fact]
    public void Search_空搜索不返回候选()
    {
        var catalog = new[]
        {
            new SwordCatalogEntry("小狐丸", "太刀", "小狐丸"),
            new SwordCatalogEntry("药研藤四郎", "短刀", "药研藤四郎"),
        };

        var results = SwordCatalogService.Search(catalog, "").Select(entry => entry.DisplayName).ToArray();

        Assert.Empty(results);
    }
}
