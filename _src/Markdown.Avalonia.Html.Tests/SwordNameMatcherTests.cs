using MFAAvalonia.Extensions.MaaFW.Custom;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class SwordNameMatcherTests
{
    [Theory]
    [InlineData("二筋贞宗", "二筋樋贞宗")]
    [InlineData("贯", "笹贯")]
    [InlineData("巴形刀", "巴形薙刀")]
    [InlineData("静形刀", "静形薙刀")]
    [InlineData("御手", "御手杵")]
    [InlineData("骨藤四郎", "骨喰藤四郎")]
    [InlineData("蜻切", "蜻蛉切")]
    [InlineData("切", "髭切")]
    [InlineData("源清", "源清麿")]
    [InlineData("童子切安纲剥落", "童子切安纲 剥落")]
    public void 模型字典缺字时仍应匹配唯一目标刀名(string ocrText, string target)
    {
        Assert.True(SwordNameMatcher.IsExactMatch(ocrText, target));
    }

    [Fact]
    public void 成员复核应把漏识喰的刀名解析为唯一标准名()
    {
        var resolved = SwordNameMatcher.FindUniqueMatchedName("骨藤四郎", ["骨喰藤四郎", "骨太刀"]);

        Assert.Equal("骨喰藤四郎", resolved);
    }

    [Fact]
    public void 成员复核遇到多个匹配候选时应保持无法确认()
    {
        var resolved = SwordNameMatcher.FindUniqueMatchedName("骨藤四郎", ["骨喰藤四郎", "骨藤四郎"]);

        Assert.Null(resolved);
    }

    [Theory]
    [InlineData("蜻蛉切", "髭切")]
    [InlineData("石切丸", "髭切")]
    [InlineData("二筋贯宗", "笹贯")]
    public void 单字缺字容错不应匹配包含相同字的其它刀名(string ocrText, string target)
    {
        Assert.False(SwordNameMatcher.IsExactMatch(ocrText, target));
    }

    [Theory]
    [InlineData("祝一号", "祝一号")]
    [InlineData("祝十号", "祝十号")]
    [InlineData("祝十一号", "祝十一号")]
    [InlineData("03松风", "松风")]
    [InlineData("小云雀x5", "小云雀")]
    [InlineData("小云雀×5", "小云雀")]
    [InlineData("小云雀＊5", "小云雀")]
    [InlineData("小云雀５", "小云雀")]
    [InlineData("×5小云雀", "小云雀")]
    [InlineData("05高楯黑", "高楯黑")]
    [InlineData("高楯黑x1", "高楯黑")]
    [InlineData("高黑", "高楯黑")]
    [InlineData("汗血・新春", "汗血・新春")]
    [InlineData("汗血・新春", "汗血新春")]
    [InlineData("汗血新春", "汗血・新春")]
    [InlineData("汗血 新春", "汗血・新春")]
    [InlineData("赤兔・新春", "赤兔新春")]
    [InlineData("超影・新春", "超影新春")]
    [InlineData("小云雀・5", "小云雀")]
    public void 马匹名应精确匹配并保留数量标记与高楯黑漏字容错(string ocrText, string target)
    {
        Assert.True(SwordNameMatcher.IsHorseMatch(ocrText, target));
    }

    [Theory]
    [InlineData("祝十号", "祝一号")]
    [InlineData("祝十一号", "祝一号")]
    [InlineData("祝一号", "祝十号")]
    [InlineData("祝二号", "祝一号")]
    [InlineData("鹿毛", "白毛")]
    [InlineData("青毛", "白毛")]
    [InlineData("超影", "超光")]
    [InlineData("汗血・新春", "汗血")]
    [InlineData("赤兔・新春", "赤兔")]
    [InlineData("超影・新春", "超影")]
    [InlineData("汗血新春", "汗血")]
    [InlineData("赤兔新春", "赤兔")]
    [InlineData("高黑", "三国黑")]
    [InlineData("高", "高楯黑")]
    public void 马匹名不应匹配其它马匹或残缺名(string ocrText, string target)
    {
        Assert.False(SwordNameMatcher.IsHorseMatch(ocrText, target));
    }
}
