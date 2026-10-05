using System;

namespace MFAAvalonia.Helper;

internal static class ResponsiveColumnCalculator
{
    internal static int CalculateColumnCount(double availableWidth, double itemWidth, int itemCount)
    {
        if (itemCount <= 1 || !double.IsFinite(availableWidth) || !double.IsFinite(itemWidth) || itemWidth <= 0)
            return 1;

        var fittingColumns = (int)Math.Floor(Math.Max(0, availableWidth) / itemWidth);
        return Math.Clamp(fittingColumns, 1, itemCount);
    }
}
