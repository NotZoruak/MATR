using System;
using System.Collections.Generic;
using System.Linq;
using MFAAvalonia.Models;
using Newtonsoft.Json.Linq;

namespace MFAAvalonia.Services;

public static class HeldSwordTableData
{
    private static readonly string[] ChineseNumbers = ["零", "一", "二", "三", "四", "五", "六"];
    private static readonly Dictionary<string, string> CharmNames = new()
    {
        ["1"] = "御守", ["2"] = "御守·极", ["3155"] = "御守·桃"
    };

    public static List<HeldSwordRow> Build(GameOverviewLogParser.Snapshot snapshot, JObject catalog)
    {
        if (!snapshot.HasFullSwordPool) return [];
        return snapshot.HeldSwords.Select(pair =>
        {
            var raw = pair.Value;
            var definition = catalog["swords"]?[raw["sword_id"]?.ToString() ?? ""] as JObject;
            var level = long.TryParse(raw["level"]?.ToString(), out var number) ? number : 0;
            var experience = long.TryParse(raw["exp"]?.ToString(), out var exp) ? exp : 0;
            var max = definition?["max_level"]?.Value<long>() ?? 0;
            var growth = definition?["grow_type"]?.ToString();
            var next = catalog["levels"]?[growth + ":" + (level + 1)]?.Value<long>();
            string? EquippedId(string key)
            {
                var id = raw[key]?.ToString();
                return long.TryParse(id, out var value) && value > 0 ? id : null;
            }

            string? EquipmentKind(string id) => snapshot.EquipmentKinds.GetValueOrDefault("equip:" + id);
            string? EquipmentName(string? kind) => kind == null ? null : catalog["equipment"]?["equip_master"]?[kind]?.ToString();

            HeldSwordRow.Troop? ReadTroop(int slot)
            {
                var id = EquippedId("equip_serial_id" + slot);
                if (id == null) return null;
                var kind = EquipmentKind(id);
                var name = EquipmentName(kind)?.Split('·')[0] ?? "未知刀装";
                var current = snapshot.EquipmentKinds.GetValueOrDefault("soldier:" + id) ?? "—";
                var maximum = kind == null ? null : catalog["equipment"]?["equip_soldier"]?[kind]?.ToString();
                return new HeldSwordRow.Troop(name, current, maximum ?? "—");
            }

            var horseId = EquippedId("horse_serial_id");
            var charmId = EquippedId("item_id");
            var treasureId = EquippedId("artifact_serial_id1");
            var treasureKind = treasureId == null ? null : snapshot.EquipmentKinds.GetValueOrDefault("artifact:" + treasureId);
            var treasureName = treasureKind == null ? null : catalog["equipment"]?["artifact_master"]?[treasureKind]?.ToString();
            var nameText = definition?["name"]?.ToString() ?? "刀剑 ID：" + raw["sword_id"];
            if (definition?["symbol"]?.ToString() == "2") nameText += "·极";
            return new HeldSwordRow
            {
                Serial = pair.Key, Data = raw, Definition = definition, Name = nameText,
                Team = FormatTeamPosition(snapshot, pair.Key),
                TeamFilter = FormatTeamFilter(snapshot, pair.Key),
                Troops = [ReadTroop(1), ReadTroop(2), ReadTroop(3)],
                Horse = horseId == null ? "—" : EquipmentName(EquipmentKind(horseId)) ?? "马匹 #" + horseId,
                Charm = charmId == null ? "—" : CharmNames.GetValueOrDefault(charmId) ?? "御守 #" + charmId,
                Treasure = treasureId == null ? "—" : treasureName ?? "未知宝物",
                NeededExperience = max > 0 && level >= max ? 0 : next.HasValue ? Math.Max(0, next.Value - experience) : null
            };
        }).ToList();
    }

    private static string FormatTeamPosition(GameOverviewLogParser.Snapshot snapshot, string serial)
    {
        if (!snapshot.SwordTeams.TryGetValue(serial, out var team)) return "未编队";
        if (!snapshot.SwordTeamPositions.TryGetValue(serial, out var position)
            || !int.TryParse(team, out var teamNumber)
            || !int.TryParse(position, out var slotNumber)
            || teamNumber < 1 || teamNumber >= ChineseNumbers.Length
            || slotNumber < 1 || slotNumber >= ChineseNumbers.Length)
            return "部队 " + team;
        return ChineseNumbers[slotNumber] + "之" + ChineseNumbers[teamNumber];
    }

    private static string FormatTeamFilter(GameOverviewLogParser.Snapshot snapshot, string serial)
    {
        if (!snapshot.SwordTeams.TryGetValue(serial, out var team)) return "未编队";
        return int.TryParse(team, out var teamNumber) && teamNumber > 0 && teamNumber < ChineseNumbers.Length
            ? "部队" + ChineseNumbers[teamNumber]
            : "部队 " + team;
    }

    public static JObject LoadCatalog()
    {
        return JObject.Parse(MATR.DefaultExtensions.CatalogResources.Read("GameSwordCatalog.json"));
    }
}
