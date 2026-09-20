using MFAAvalonia.Extensions.MaaFW.Custom;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class ClickTopRepairableSwordActionTests
{
    [Fact]
    public void ContainsColorInRoi_命中蓝色像素时返回真()
    {
        var pixels = new byte[2 * 1 * 4];
        pixels[0] = 180;
        pixels[1] = 84;
        pixels[2] = 23;
        pixels[3] = 255;

        var matched = ClickTopRepairableSwordAction.ContainsColorInRoi(
            pixels,
            2,
            1,
            [23, 84, 180],
            [23, 84, 180]);

        Assert.True(matched);
    }

    [Fact]
    public void ContainsColorInRoi_没有命中蓝色像素时返回假()
    {
        var pixels = new byte[] { 0, 0, 0, 255 };

        var matched = ClickTopRepairableSwordAction.ContainsColorInRoi(
            pixels,
            1,
            1,
            [23, 84, 180],
            [23, 84, 180]);

        Assert.False(matched);
    }
}
