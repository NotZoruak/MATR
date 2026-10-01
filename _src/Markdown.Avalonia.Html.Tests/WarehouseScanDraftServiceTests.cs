using MFAAvalonia.Services;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class WarehouseScanDraftServiceTests
{
    [Theory]
    [InlineData("套纸笔", "一套纸笔")]
    [InlineData("狮子螺钾鞍", "狮子螺钿鞍")]
    [InlineData("口团子", "一口团子")]
    public void NormalizeOtherItemName_修正已知物品名称误识别(string ocrName, string expectedName)
    {
        Assert.Equal(expectedName, WarehouseScanDraftService.NormalizeOtherItemName(ocrName));
    }
}
