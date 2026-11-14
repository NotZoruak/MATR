using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace MFAAvalonia.Controls;

/// <summary>表格到达纵向滚动边界后，将滚轮事件交给外层滚动容器。</summary>
public class ChainedScrollDataGrid : DataGrid
{
    // 复用标准表格主题，确保模板、表头和数据行正常呈现。
    protected override Type StyleKeyOverride => typeof(DataGrid);

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        // 已被单元格控件处理或横向滚动的事件沿用原行为。
        var releaseAtBoundary = false;
        if (!e.Handled && e.Delta.X == 0 && e.Delta.Y != 0
            && !e.KeyModifiers.HasFlag(KeyModifiers.Shift)
            && ScrollViewer.GetIsScrollChainingEnabled(this))
        {
            var scrollbar = this.GetVisualDescendants().OfType<ScrollBar>()
                .FirstOrDefault(bar => bar.Name == "PART_VerticalScrollbar"
                    && bar.Orientation == Orientation.Vertical);
            if (scrollbar != null)
            {
                // 滚动条显示值与表格内部偏移可能存在浮点尾差。
                const double tolerance = 0.001;
                releaseAtBoundary = e.Delta.Y < 0
                    ? scrollbar.Value >= scrollbar.Maximum - tolerance
                    : scrollbar.Value <= scrollbar.Minimum + tolerance;
            }
        }

        base.OnPointerWheelChanged(e);
        if (releaseAtBoundary)
            e.Handled = false;
    }
}
