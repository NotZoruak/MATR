using MFAAvalonia.Models;
using MFAAvalonia.ViewModels.Pages;
using System;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class WorkRecordsViewModelTests
{
    [Fact]
    public void SelectedLogisticsDispatchCountText_按地图编号汇总并排序()
    {
        var record = new WorkRecord();
        record.LogisticsCounts["派遣远征"] = 4;
        record.LogisticsDispatches.Add(new LogisticsDispatch(new DateTime(2026, 10, 1, 10, 0, 0), "部队1", "3-2"));
        record.LogisticsDispatches.Add(new LogisticsDispatch(new DateTime(2026, 10, 1, 10, 1, 0), "部队2", "2-4"));
        record.LogisticsDispatches.Add(new LogisticsDispatch(new DateTime(2026, 10, 1, 10, 2, 0), "部队3", "2-1"));
        record.LogisticsDispatches.Add(new LogisticsDispatch(new DateTime(2026, 10, 1, 10, 3, 0), "部队4", "2-1"));
        var viewModel = new WorkRecordsViewModel { SelectedRecord = record };

        Assert.Equal("派遣远征 ×4    2-1 ×2    2-4 ×1    3-2 ×1", viewModel.SelectedLogisticsDispatchCountText);
    }

    [Fact]
    public void SelectedRecordChanged_派遣明细恢复收起()
    {
        var viewModel = new WorkRecordsViewModel { SelectedRecord = new WorkRecord() };
        viewModel.ToggleLogisticsDispatchCommand.Execute(null);

        Assert.True(viewModel.IsLogisticsDispatchExpanded);

        viewModel.SelectedRecord = new WorkRecord();

        Assert.False(viewModel.IsLogisticsDispatchExpanded);
    }
}
