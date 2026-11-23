using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using MFAAvalonia.Views.Pages;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class HarvestKindPickerTests
{
    [Fact]
    public async Task 下拉选择切换三类收获且全部时间位于日期左侧()
    {
        await ExtensionUiTests.Session.Dispatch(() =>
        {
            var view = new HarvestRecordsView(Path.GetTempPath());
            var picker = view.FindControl<ComboBox>("HarvestKindPicker")!;
            var grid = view.FindControl<DataGrid>("RecordsGrid")!;
            var duty = view.FindControl<ContentControl>("DutyContent")!;
            Assert.Equal(1, picker.SelectedIndex);
            Assert.Equal("物品", grid.Columns[1].Header);
            picker.SelectedIndex = 0;
            Assert.Equal("刀剑名", grid.Columns[1].Header);
            picker.SelectedIndex = 2;
            Assert.True(duty.IsVisible);
            Assert.False(grid.IsVisible);
            picker.SelectedIndex = 1;
            Assert.False(duty.IsVisible);
            Assert.True(grid.IsVisible);
            Assert.True(Grid.GetColumn(view.FindControl<Button>("AllTimeButton")!) <
                        Grid.GetColumn(view.FindControl<CalendarDatePicker>("StartDatePicker")!));
        }, CancellationToken.None);
    }
}
