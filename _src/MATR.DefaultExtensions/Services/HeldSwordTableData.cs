using System;
using System.Collections.Generic;
using System.Linq;
using MFAAvalonia.Models;
using Newtonsoft.Json.Linq;

namespace MFAAvalonia.Services;

public static class HeldSwordTableData
{
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
            var nameText = definition?["name"]?.ToString() ?? "刀剑 ID：" + raw["sword_id"];
            if (definition?["symbol"]?.ToString() == "2") nameText += "·极";
            return new HeldSwordRow
            {
                Serial = pair.Key, Data = raw, Definition = definition, Name = nameText,
                Team = snapshot.SwordTeams.TryGetValue(pair.Key, out var team) ? "部队 " + team : "未编队",
                Troops = [ReadTroop(1), ReadTroop(2), ReadTroop(3)],
                Horse = horseId == null ? "—" : EquipmentName(EquipmentKind(horseId)) ?? "马匹 #" + horseId,
                Charm = charmId == null ? "—" : "御守 #" + charmId,
                NeededExperience = max > 0 && level >= max ? 0 : next.HasValue ? Math.Max(0, next.Value - experience) : null
            };
        }).ToList();
    }

    public static JObject LoadCatalog()
    {
        return JObject.Parse(MATR.DefaultExtensions.CatalogResources.Read("GameSwordCatalog.json"));
    }
}
