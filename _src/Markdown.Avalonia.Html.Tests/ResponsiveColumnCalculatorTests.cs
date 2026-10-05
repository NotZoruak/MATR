using MFAAvalonia.Helper;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class ResponsiveColumnCalculatorTests
{
    [Theory]
    [InlineData(600, 180, 16, 3)]
    [InlineData(500, 180, 16, 2)]
    [InlineData(600, 180, 2, 2)]
    [InlineData(0, 180, 16, 1)]
    public void 按可用宽度和选项宽度计算列数(double availableWidth, double itemWidth, int itemCount, int expected)
    {
        var actual = ResponsiveColumnCalculator.CalculateColumnCount(availableWidth, itemWidth, itemCount);
        Assert.Equal(expected, actual);
    }
}
