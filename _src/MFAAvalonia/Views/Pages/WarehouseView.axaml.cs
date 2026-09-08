using Avalonia.Controls;
using Avalonia;
using Avalonia.Input;
using Avalonia.VisualTree;
using MFAAvalonia.ViewModels.Pages;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace MFAAvalonia.Views.Pages;

/// <summary>仓库页面。</summary>
public partial class WarehouseView : UserControl
{
    private WarehouseOtherItemViewModel? _dragItem;
    private IPointer? _dragPointer;
    private double _dragStartY;
    private bool _isDraggingOtherItem;

    public WarehouseView()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<WarehouseViewModel>();
    }

    private void ChartPoint_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not WarehouseChartPointViewModel point)
            return;

        if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
            return;

        if (point.SelectCommand.CanExecute(null))
            point.SelectCommand.Execute(null);

        e.Handled = true;
    }

    private void OtherItemDragHandle_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control handle || handle.DataContext is not WarehouseOtherItemViewModel item)
            return;

        var point = e.GetCurrentPoint(handle);
        if (!point.Properties.IsLeftButtonPressed)
            return;

        _dragItem = item;
        _dragPointer = e.Pointer;
        _dragStartY = e.GetPosition(OtherItemsList).Y;
        _isDraggingOtherItem = false;
        e.Pointer.Capture(handle);
        e.Handled = true;
    }

    private void OtherItemDragHandle_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragItem == null || _dragPointer != e.Pointer || !e.GetCurrentPoint(OtherItemsList).Properties.IsLeftButtonPressed)
            return;

        var position = e.GetPosition(OtherItemsList);
        if (!_isDraggingOtherItem && Math.Abs(position.Y - _dragStartY) < 8)
            return;

        _isDraggingOtherItem = true;
        ScrollOtherItemsWhileDragging(e);
        e.Handled = true;
    }

    private void OtherItemDragHandle_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragItem != null && DataContext is WarehouseViewModel viewModel)
        {
            var sourceIndex = viewModel.OtherItems.IndexOf(_dragItem);
            var targetIndex = GetOtherItemTargetIndex(e.GetPosition(OtherItemsList), viewModel.OtherItems.Count);
            if (sourceIndex >= 0 && targetIndex >= 0)
                viewModel.MoveOtherItem(sourceIndex, targetIndex);
        }

        if (_dragPointer == e.Pointer)
            e.Pointer.Capture(null);

        _dragItem = null;
        _dragPointer = null;
        _isDraggingOtherItem = false;
        e.Handled = true;
    }

    private int GetOtherItemTargetIndex(Point position, int itemCount)
    {
        var targetIndex = 0;
        for (var index = 0; index < itemCount; index++)
        {
            if (OtherItemsList.ContainerFromIndex(index) is not Control container)
                continue;

            var center = container.TranslatePoint(new Point(0, container.Bounds.Height / 2), OtherItemsList);
            if (center == null)
                continue;

            if (position.Y < center.Value.Y)
                return index;

            targetIndex = index;
        }

        return targetIndex;
    }

    private void ScrollOtherItemsWhileDragging(PointerEventArgs e)
    {
        var position = e.GetPosition(OtherItemsScrollViewer);
        const double edgeDistance = 36;
        const double scrollStep = 28;
        var offset = OtherItemsScrollViewer.Offset;
        var maxOffsetY = Math.Max(0, OtherItemsScrollViewer.Extent.Height - OtherItemsScrollViewer.Viewport.Height);

        if (position.Y <= edgeDistance && offset.Y > 0)
        {
            OtherItemsScrollViewer.Offset = new Vector(offset.X, Math.Max(0, offset.Y - scrollStep));
        }
        else if (position.Y >= OtherItemsScrollViewer.Bounds.Height - edgeDistance && offset.Y < maxOffsetY)
        {
            OtherItemsScrollViewer.Offset = new Vector(offset.X, Math.Min(maxOffsetY, offset.Y + scrollStep));
        }
    }
}
