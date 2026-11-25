using MFAAvalonia.Models;
using MFAAvalonia.Services;
using Newtonsoft.Json.Linq;
using Xunit;
using System.Linq;
using MATR.GameLogReader;
using MATR.GameLogReaderSdk;

namespace Markdown.Avalonia.Html.Tests;

public class HeldSwordTests
{
    [Fact]
    public void 部队机动合计刀装马匹和强化后的宝物且不改写本体()
    {
        var snapshot = new GameOverviewSnapshot { HasFullSwordPool = true };
        snapshot.HeldSwords["1"] = JObject.Parse("{\"sword_id\":3,\"mobile\":74,\"equip_serial_id1\":11,\"equip_serial_id2\":12,\"horse_serial_id\":20,\"artifact_serial_id1\":30}");
        snapshot.EquipmentKinds["equip:11"] = "9";
        snapshot.EquipmentKinds["equip:12"] = "9";
        snapshot.EquipmentKinds["equip:20"] = "10008";
        snapshot.EquipmentKinds["mobile:artifact:30"] = "9";
        var row = Assert.Single(HeldSwordTableData.Build(snapshot, HeldSwordTableData.LoadCatalog()));
        Assert.Equal(110, row.EquippedMobility);
        Assert.Equal(74, row.Number("mobile"));
        Assert.Contains("刀装：10", row.MobilityTip);
        Assert.Contains("马匹：17", row.MobilityTip);
        snapshot.EquipmentKinds.Remove("mobile:artifact:30");
        Assert.Null(Assert.Single(HeldSwordTableData.Build(snapshot, HeldSwordTableData.LoadCatalog())).EquippedMobility);
        snapshot.HeldSwords["1"]["artifact_serial_id1"] = 0;
        Assert.Equal(101, Assert.Single(HeldSwordTableData.Build(snapshot, HeldSwordTableData.LoadCatalog())).EquippedMobility);
    }

    [Fact]
    public void 宝物机动响应保留强化数值且不保存备注()
    {
        var snapshot = GameOverviewLogParser.Parse("2026-11-23 14:00:00 S->C /sally?uid=1 status:200 data: {\"status\":0,\"artifact\":{\"30\":{\"artifact_id\":1,\"level\":5,\"mobile\":9,\"comment\":\"不应保存\"}}}");
        Assert.Equal("9", snapshot.EquipmentKinds["mobile:artifact:30"]);
        Assert.DoesNotContain("comment", string.Join(" ", snapshot.EquipmentKinds.Keys));
    }

    [Fact]
    public void 等级降序按累计经验排列而不优先等级()
    {
        var rows = new[]
        {
            new HeldSwordRow { Serial = "1", Name = "高等级低经验", Data = JObject.Parse("{\"level\":99,\"exp\":1000}") },
            new HeldSwordRow { Serial = "2", Name = "低等级高经验", Data = JObject.Parse("{\"level\":80,\"exp\":2000}") }
        };
        var sorted = rows.OrderByDescending(row => (long?)row.SortingValue("Level")).ToArray();
        Assert.Equal("2", sorted[0].Serial);
        Assert.Equal(80L, sorted[0].SortValue("Level", false));
        Assert.Equal(2000L, sorted[0].SortValue("Level", true));
    }

    [Fact]
    public void 局部刀剑更新不会覆盖全量刀池()
    {
        var log = "2026-11-17 10:00:00 S->C /sally?uid=1 status:200 data: {\"status\":0,\"sword_all\":{\"11\":{\"sword_id\":3,\"level\":1},\"12\":{\"sword_id\":5,\"level\":2}}}\n" +
            "2026-11-17 10:01:00 S->C /repair?uid=1 status:200 data: {\"status\":0,\"sword\":{\"11\":{\"sword_id\":3,\"level\":4}}}";
        var result = GameOverviewLogParser.Parse(log);
        Assert.True(result.HasFullSwordPool);
        Assert.Equal(2, result.HeldSwords.Count);
        Assert.Equal("4", result.HeldSwords["11"]["level"]?.ToString());
    }

    [Fact]
    public void 新全量刀池能够删除已不持有的刀剑()
    {
        var log = "2026-11-17 10:00:00 S->C /sally?uid=1 status:200 data: {\"status\":0,\"sword_all\":{\"11\":{\"sword_id\":3}}}\n" +
            "2026-11-17 10:01:00 S->C /party/list?uid=1 status:200 data: {\"status\":0,\"sword\":{}}";
        var result = GameOverviewLogParser.Parse(log);
        Assert.True(result.HasFullSwordPool);
        Assert.Empty(result.HeldSwords);
    }

    [Fact]
    public void 内番显示已提升除以总量而不是剩余量()
    {
        var row = new HeldSwordRow
        {
            Serial = "1", Name = "测试刀剑",
            Data = JObject.Parse("{\"protect\":2,\"hp_max\":45,\"hp_up\":3,\"atk\":89,\"atk_up\":0}"),
            Definition = JObject.Parse("{\"hp_up\":10}")
        };
        Assert.Equal("喜爱", row.Protection);
        Assert.Equal((7L, 10L), row.Bonus("hp")!.Value);
        Assert.False(row.IsMaximum("hp"));
        Assert.True(row.IsMaximum("atk"));
    }

    [Fact]
    public void 升级经验使用静态累计经验阈值()
    {
        var snapshot = new GameOverviewSnapshot { HasFullSwordPool = true };
        snapshot.HeldSwords["1"] = JObject.Parse("{\"sword_id\":3,\"level\":1,\"exp\":40}");
        var catalog = JObject.Parse("{\"swords\":{\"3\":{\"name\":\"测试\",\"grow_type\":0,\"max_level\":99}},\"levels\":{\"0:2\":100}}");
        var row = Assert.Single(HeldSwordTableData.Build(snapshot, catalog));
        Assert.Equal(60, row.NeededExperience);
        Assert.Contains("40", row.LevelTip);
    }

    [Fact]
    public void 保护与部队等字段不采集凭据()
    {
        var result = GameOverviewLogParser.Parse("2026-11-17 10:00:00 S->C /sally?uid=1 status:200 data: {\"status\":0,\"sword_all\":{\"11\":{\"sword_id\":3,\"secret\":\"不应保存\"}},\"party\":{\"2\":{\"slot\":{\"1\":{\"serial_id\":11}}}}}");
        Assert.Equal("2", result.SwordTeams["11"]);
        Assert.Null(result.HeldSwords["11"]["secret"]);
    }
}
