using MFAAvalonia.Extensions.MaaFW.Custom;
using Newtonsoft.Json.Linq;
using System;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class FatigueCheckActionTests
{
    [Theory]
    [InlineData(0, 839, 190, 77, 22)]
    [InlineData(1, 839, 284, 77, 22)]
    [InlineData(2, 839, 379, 77, 22)]
    [InlineData(3, 839, 473, 77, 22)]
    [InlineData(4, 839, 568, 77, 22)]
    [InlineData(5, 839, 662, 77, 22)]
    [InlineData(6, 340, 187, 80, 22)]
    [InlineData(7, 340, 282, 80, 22)]
    [InlineData(8, 340, 376, 80, 22)]
    [InlineData(9, 340, 471, 80, 22)]
    [InlineData(10, 340, 565, 80, 22)]
    [InlineData(11, 340, 660, 80, 22)]
    public void 疲劳OCR请求应使用OnlyRec并保留对应区域(int index, int x, int y, int width, int height)
    {
        var roi = index < 6
            ? FatigueRecognitionHelper.FatigueRoisExpedition[index]
            : FatigueRecognitionHelper.FatigueRoisSortie[index - 6];

        var requestJson = FatigueRecognitionHelper.CreateFatigueOcrNode(roi).ToJson();

        var request = JObject.Parse(requestJson!)["FatigueCheckOcr"]!;

        Assert.True(request["only_rec"]!.Value<bool>());
        Assert.Equal([x, y, width, height], request["roi"]!.Values<int>());
    }

    [Theory]
    [InlineData("91/100", 91)]
    [InlineData("B1/100", 81)]
    [InlineData("O/100", 0)]
    public void 疲劳OCR文本应解析斜线前的数值并修正常见误识别(string text, int expected)
    {
        Assert.Equal(expected, FatigueRecognitionHelper.ParseFatigueValue(text));
    }

    [Fact]
    public void 无有效疲劳数字时应返回空值()
    {
        Assert.Null(FatigueRecognitionHelper.ParseFatigueValue("--/100"));
    }

    [Fact]
    public void 最低疲劳值应忽略无法识别的位置()
    {
        Assert.Equal((2, 73), FatigueRecognitionHelper.FindLowest([null, 88, 73, null, 91, null]));
    }

    [Theory]
    [InlineData(87, 91, 87)]
    [InlineData(0, 91, 91)]
    [InlineData(null, 91, 91)]
    public void 阈值无效或未提供时应使用默认值(int? configuredThreshold, int defaultThreshold, int expected)
    {
        Assert.Equal(expected, FatigueRecognitionHelper.ResolveThreshold(configuredThreshold, defaultThreshold));
    }
}
