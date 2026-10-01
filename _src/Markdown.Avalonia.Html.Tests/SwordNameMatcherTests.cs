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

    [Theory]
    [InlineData("蜻蛉切", "髭切")]
    [InlineData("石切丸", "髭切")]
    [InlineData("二筋贯宗", "笹贯")]
    public void 单字缺字容错不应匹配包含相同字的其它刀名(string ocrText, string target)
    {
        Assert.False(SwordNameMatcher.IsExactMatch(ocrText, target));
    }
}
