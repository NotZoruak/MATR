using Avalonia.Controls;
using MFAAvalonia.ViewModels.Pages;
using MFAAvalonia.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using Avalonia.Controls.Selection;

namespace MFAAvalonia.Views.Pages;

/// <summary>工作记录页：左侧运行记录列表 + 右侧统计卡片</summary>
public partial class WorkRecordsView : UserControl
{
    private bool _syncingSelection;
    private WorkRecordsViewModel ViewModel => (WorkRecordsViewModel)DataContext!;

    public WorkRecordsView()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<WorkRecordsViewModel>();
        RecordsColumn.SizeChanged += (_, args) =>
        {
            if (args.NewSize.Height > 0)
                SavedRecordsCard.MaxHeight = Math.Max(0, (args.NewSize.Height - 10) / 2);
        };
    }

    private void LogRecordsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection)
            return;
        if (sender is not ListBox listBox)
            return;
        _syncingSelection = true;
        SavedRecordsList.SelectedItems.Clear();
        _syncingSelection = false;
        ViewModel.SetSelectedLogRecords(listBox.SelectedItems.Cast<WorkRecord>());
    }

    private void SavedRecordsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection)
            return;
        if (sender is not ListBox listBox)
            return;
        _syncingSelection = true;
        LogRecordsList.SelectedItems.Clear();
        _syncingSelection = false;
        ViewModel.SetSelectedSavedRecords(listBox.SelectedItems.Cast<SavedWorkRecord>());
    }
}
