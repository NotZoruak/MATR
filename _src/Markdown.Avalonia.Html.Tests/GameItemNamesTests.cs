using MFAAvalonia.Services;
using Xunit;
using MATR.GameLogReader;
using MATR.GameLogReaderSdk;

namespace Markdown.Avalonia.Html.Tests;

public class GameItemNamesTests
{
    [Theory]
    [InlineData("1", "1", "御守")]
    [InlineData("3", "1", "投石兵·中")]
    [InlineData("5", "1", "委托符")]
    [InlineData("50", "1", "甲州金")]
    [InlineData("1", "6101", "曜变天目碎片")]
    [InlineData("1", "4206", "天竺牡丹")]
    public void 按类型及编号查找名称(string type, string id, string expected)
        => Assert.Equal(expected, GameItemNames.Find(type, id));

    [Fact]
    public void 旧记录恢复名称且未知类型不串名()
    {
        Assert.Equal("御守", GameItemNames.DisplayName("未知物品（类型 1，ID 1）"));
        Assert.Equal("未知物品（类型 999，ID 6101）", GameItemNames.DisplayName("未知物品（类型 999，ID 6101）"));
        Assert.Null(GameItemNames.Find("3", "6101"));
    }

    [Fact]
    public void 获取记录使用静态名称且保留未知编号()
    {
        var snapshot = GameOverviewLogParser.Parse("2026-11-23 14:51:47 S->C /mission/rewards?uid=1 status:200 data:{\"status\":0,\"reward\":[{\"item_type\":1,\"item_id\":1,\"item_num\":2},{\"item_type\":50,\"item_id\":1,\"item_num\":3},{\"item_type\":999,\"item_id\":1,\"item_num\":4}]}\n");
        Assert.Collection(snapshot.HarvestRecords,
            entry => { Assert.Equal("御守", entry.Name); Assert.Equal(2, entry.Amount); },
            entry => { Assert.Equal("甲州金", entry.Name); Assert.Equal(3, entry.Amount); },
            entry => Assert.Equal("未知物品（类型 999，ID 1）", entry.Name));
    }
}
