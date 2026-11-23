using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using MFAAvalonia.Services;
using MFAAvalonia.Views.Pages;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public sealed class DutyRecordsSettlementTests
{
    [Fact]
    public async Task 内番两列分别显示变化符号与提升比例且映射六种结算类型()
    {
        await ExtensionUiTests.Session.Dispatch(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "MATR-duty-results-" + Guid.NewGuid().ToString("N"));
            try
            {
                var catalog = HeldSwordTableData.LoadCatalog();
                var sword = (JObject)catalog["swords"]!["3"]!;
                var hpTotal = sword.Value<int>("hp_up");
                var scoutTotal = sword.Value<int>("scout_up");
                var view = new DutyRecordsView(directory);
                var data = new JObject { ["hp_up"] = hpTotal - 3, ["scout_up"] = scoutTotal - 1 };
                var record = new GameOverviewLogParser.DutyRecord("test", "2026-11-23 15:59:57", "耕作", "1", "3", 4, 1, true, false, data);
                view.AddRecords("测试账号", [record]);
                var grid = view.FindControl<StackPanel>("GroupsPanel")!.Children.OfType<StackPanel>().Single().Children.OfType<DataGrid>().Single();
                Assert.Contains(grid.Columns, column => Equals(column.Header, "结算结果 1"));
                Assert.Contains(grid.Columns, column => Equals(column.Header, "结算结果 2"));
                var row = Assert.Single(grid.ItemsSource!.Cast<object>());
                var first = row.GetType().GetProperty("First")!.GetValue(row)!;
                var second = row.GetType().GetProperty("Second")!.GetValue(row)!;
                Assert.Equal($"生存+1（3/{hpTotal}）", first.GetType().GetProperty("Text")!.GetValue(first));
                Assert.Equal(true, first.GetType().GetProperty("Increased")!.GetValue(first));
                Assert.Equal(false, second.GetType().GetProperty("Increased")!.GetValue(second));
                Assert.Equal($"侦察-（1/{scoutTotal}）", second.GetType().GetProperty("Text")!.GetValue(second));
                var names = new[] { "机动", "防御", "侦察", "生存", "打击", "冲力" };
                for (var type = 1; type <= 6; type++)
                {
                    view.ClearRecords();
                    var duty = type <= 2 ? "饲马" : type <= 4 ? "耕作" : "手合";
                    view.AddRecords("测试账号", [record with { DutyType = duty, UpType = type }]);
                    Assert.Equal(1, view.CreateSummary().Gains[duty + " · " + names[type - 1]]);
                }
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }, CancellationToken.None);
    }
}
