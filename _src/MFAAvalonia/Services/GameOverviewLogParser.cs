using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace MFAAvalonia.Services;

/// <summary>仅提取已核对的总览字段，不保存凭据或原始响应。</summary>
public static class GameOverviewLogParser
{
    private static readonly string[] SwordFields = ["sword_id", "level", "exp", "ranbu_level", "hp", "hp_max", "fatigue", "protect", "hp_up", "atk", "atk_up", "def", "def_up", "mobile", "mobile_up", "back", "back_up", "scout", "scout_up", "hide", "hide_up", "loyalties", "created_at", "equip_serial_id1", "equip_serial_id2", "equip_serial_id3", "horse_serial_id", "item_id", "artifact_serial_id1", "artifact_serial_id2"];
    public sealed record Value(string Text, string ObservedAt);
    public sealed record TeamEntry(string Status, string? FinishedAt);
    public sealed record WorkEntry(string Label, string? FinishedAt);
    public sealed class Snapshot
    {
        public string Account { get; set; } = "";
        public Dictionary<string, Value> Values { get; } = new();
        public Dictionary<string, List<WorkEntry>> WorkItems { get; } = new();
        public Dictionary<string, List<string?>> DutyMembers { get; } = new();
        public Dictionary<string, TeamEntry> Teams { get; } = new();
        public Dictionary<string, JObject> HeldSwords { get; } = new();
        public Dictionary<string, string> SwordTeams { get; } = new();
        public Dictionary<string, string> SwordTeamPositions { get; } = new();
        public Dictionary<string, string> EquipmentKinds { get; } = new();
        public bool HasFullSwordPool { get; set; }
        public string SwordObservedAt { get; set; } = "";
        public int Responses { get; set; }
    }

    public static Snapshot Parse(string log)
    {
        var result = new Snapshot();
        var teams = new Dictionary<string, (string Status, string? Finish, string Time)>();
        var maps = new Dictionary<string, string>();
        var swords = new Dictionary<string, string>();
        JObject? dutyDetails = null;
        int? repairCapacity = null;
        var requests = new Dictionary<string, JObject>();
        foreach (var line in log.Split('\n'))
        {
            var response = line.Contains("S->C", StringComparison.Ordinal);
            var request = line.Contains("C->S", StringComparison.Ordinal);
            if (!response && !request) continue;
            var start = line.IndexOf('{');
            if (start < 0) continue;
            JObject data;
            try { data = JObject.Parse(line[start..]); }
            catch (Newtonsoft.Json.JsonException) { continue; }
            var route = Regex.Match(line[..start], @"(/[a-zA-Z_]+(?:/[a-zA-Z_]+)*)(?:\?|\s)");
            if (!route.Success) continue;
            var path = route.Groups[1].Value;
            var account = Regex.Match(line[..start], @"[?&]uid=(\d+)").Groups[1].Value;
            if (path == "/login/start" && data["user_id"] != null)
                account = data["user_id"]!.ToString();
            if (request && account.Length == 0) account = data["uid"]?.ToString() ?? "";
            if (account.Length == 0) continue;
            if (request)
            {
                if (path == "/conquest/start") requests[account] = data;
                continue;
            }
            if (!Regex.IsMatch(line[..start], @"status:200\b") || data["status"]?.ToString() != "0") continue;
            if (result.Account != account)
            {
                result = new Snapshot { Account = account };
                teams.Clear();
                maps.Clear();
                swords.Clear();
                dutyDetails = null;
                repairCapacity = null;
            }
            result.Responses++;
            var time = Regex.Match(line, @"\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}").Value;
            void Set(string key, string? value)
            {
                if (value != null) result.Values[key] = new Value(value, time);
            }
            if (int.TryParse((data["repair_slot_max"] ?? data["repair_slot"])?.ToString(), out var capacity) && capacity is >= 0 and <= 100)
                repairCapacity = capacity;
            var fullPool = path == "/sally" ? data["sword_all"] as JObject
                : path == "/party/list" ? data["sword"] as JObject : null;
            if (fullPool != null)
            {
                result.HeldSwords.Clear();
                result.HasFullSwordPool = true;
            }
            foreach (var swordField in new[] { "sword_all", "sword" })
                if (data[swordField] is JObject pool)
                    foreach (var property in pool.Properties())
                    {
                        if (property.Value is not JObject raw || raw["sword_id"] == null) continue;
                        var serial = raw["serial_id"]?.ToString() ?? property.Name;
                        if (!long.TryParse(serial, out var serialNumber) || serialNumber <= 0) continue;
                        if (!result.HeldSwords.TryGetValue(serial, out var item))
                            result.HeldSwords[serial] = item = new JObject();
                        item["serial_id"] = serial;
                        foreach (var field in SwordFields)
                            if (raw.TryGetValue(field, out var value)) item[field] = value.DeepClone();
                        result.SwordObservedAt = time;
                    }
            foreach (var equipmentField in new[] { "equip", "artifact" })
                if (data[equipmentField] is JObject equipment)
                    foreach (var property in equipment.Properties())
                        if (property.Value is JObject item)
                        {
                            var id = item["serial_id"]?.ToString() ?? property.Name;
                            var key = equipmentField + ":" + id;
                            if ((item["equip_id"] ?? item["artifact_id"]) is { } kind)
                                result.EquipmentKinds[key] = kind.ToString();
                            if (equipmentField == "equip" && long.TryParse(item["soldier"]?.ToString(), out var soldier))
                                result.EquipmentKinds["soldier:" + id] = soldier.ToString();
                        }
            foreach (var swordField in new[] { "sword_all", "sword" })
                if (data[swordField] is JObject records)
                    foreach (var property in records.Properties())
                        if (property.Value is JObject sword && sword["sword_id"] is { } id)
                            swords[sword["serial_id"]?.ToString() ?? property.Name] = id.ToString();
            if (path == "/login/start")
            {
                Set("PlayerName", data["name"]?.ToString());
                Set("Country", data["server_name"]?.ToString());
                Set("Castle", data["castle_name"]?.ToString());
                Set("Level", data["level"]?.ToString());
                if (data["secretary"] != null) Set("Secretary", "刀剑 ID：" + data["secretary"]);
            }
            if (data["resource"] is JObject resource)
                foreach (var pair in new[] { ("charcoal", "Charcoal"), ("steel", "Steel"), ("coolant", "Coolant"), ("file", "File"), ("bill", "Bill") })
                    if (long.TryParse(resource[pair.Item1]?.ToString(), out var amount))
                        Set(pair.Item2, amount.ToString("N0", CultureInfo.InvariantCulture));
            if (data["currency"] is JObject currency && long.TryParse(currency["money"]?.ToString(), out var money))
                Set("Money", money.ToString("N0", CultureInfo.InvariantCulture));
            if (data["item"] is JObject items)
                foreach (var item in items.Properties().Select(x => x.Value).OfType<JObject>())
                    if (item["consumable_id"]?.ToString() == "8" && long.TryParse(item["num"]?.ToString(), out var count))
                        Set("Accelerator", count.ToString("N0", CultureInfo.InvariantCulture));
            if (data["assist_item_id"]?.ToString() == "8" && long.TryParse(data["assist_item_num"]?.ToString(), out var remaining))
                Set("Accelerator", remaining.ToString("N0", CultureInfo.InvariantCulture));
            if (data["party"] is JObject parties && path is "/login/start" or "/home/situation" or "/sally" or "/party/list" or "/conquest" or "/conquest/start" or "/conquest/complete" or "/conquest/cancel")
                foreach (var party in parties.Properties())
                {
                    if (party.Value is not JObject item) continue;
                    var number = item["party_no"]?.ToString() ?? party.Name;
                    if (!int.TryParse(number, out var n) || n < 1 || n > 5) continue;
                    if (item["slot"] is JObject members)
                    {
                        foreach (var serial in result.SwordTeams.Where(x => x.Value == number).Select(x => x.Key).ToArray())
                        {
                            result.SwordTeams.Remove(serial);
                            result.SwordTeamPositions.Remove(serial);
                        }
                        foreach (var (member, index) in members.Properties().Select((value, index) => (value, index)))
                            if (member.Value is JObject sword
                                && sword["serial_id"] is { } serial
                                && serial.Type != JTokenType.Null)
                            {
                                var serialId = serial.ToString();
                                result.SwordTeams[serialId] = number;
                                var position = int.TryParse(member.Name, out var slotNumber) ? slotNumber : index + 1;
                                result.SwordTeamPositions[serialId] = position.ToString(CultureInfo.InvariantCulture);
                            }
                    }
                    if (item["status"] != null)
                    {
                        var status = item["status"]!.ToString();
                        var finish = item["finished_at"]?.ToString();
                        if (finish == null && teams.TryGetValue(number, out var previous) && previous.Status == status)
                            finish = previous.Finish;
                        teams[number] = (status, finish, time);
                        if (status != "2") maps.Remove(number);
                    }
                }
            if (path is "/conquest" or "/conquest/start" or "/conquest/complete" or "/conquest/cancel" && data["summary"] is JContainer summary)
            {
                maps.Clear();
                var rows = summary is JObject obj ? obj.Properties().Select(x => x.Value) : summary.Children();
                foreach (var row in rows.OfType<JObject>())
                    if (row["party_no"] is { } no && row["field_id"] is { } field)
                        maps[no.ToString()] = ExpeditionMap(field.ToString());
            }
            if (path == "/conquest/start")
            {
                // 只在成功响应后采信派遣请求，不能将失败派遣当作实际目的地。
                var source = data["party_no"] != null && data["field_id"] != null ? data : requests.GetValueOrDefault(account);
                if (source?["party_no"] is { } no && source["field_id"] is { } field)
                    maps[no.ToString()] = ExpeditionMap(field.ToString());
                requests.Remove(account);
            }
            if (path == "/conquest/complete" && data["party_no"] is { } completedParty)
                maps.Remove(completedParty.ToString());
            if (path is "/login/start" or "/home" or "/home/situation" or "/forge" or "/repair" or "/duty" or "/duty/start" or "/duty/complete")
            {
                if (path == "/duty/complete")
                {
                    dutyDetails = null;
                    result.WorkItems["Duty"] = [];
                    Set("Duty", "未使用");
                }
                else if (data["duty"] is JObject duty)
                {
                    dutyDetails = duty;
                    result.WorkItems["Duty"] = [new WorkEntry("", duty["finished_at"]?.ToString())];
                    Set("Duty", "已读取");
                }
                foreach (var pair in new[] { ("forge", "Forge"), ("repair", "Repair") })
                    if (data[pair.Item1] is JContainer slots)
                    {
                        var entries = slots is JObject obj ? obj.Properties().Select(x => x.Value).ToArray() : slots.Children().ToArray();
                        result.WorkItems[pair.Item2] = entries.OfType<JObject>().Select((x, i) =>
                            new WorkEntry("锻冶所 " + (x["slot_no"]?.ToString() ?? (i + 1).ToString()), x["finished_at"]?.ToString())).ToList();
                        Set(pair.Item2, entries.Length == 0 ? "未使用" : "已读取");
                    }
            }
        }
        if (dutyDetails != null)
            foreach (var job in new[] { "horse", "field", "bout" })
            {
                var members = new List<string?>();
                for (var index = 1; index <= 2; index++)
                {
                    var serial = dutyDetails[job + "_id" + index]?.ToString();
                    var id = dutyDetails[job + "_sword_id" + index]?.ToString();
                    if (string.IsNullOrEmpty(id) || id == "0")
                        id = string.IsNullOrEmpty(serial) || serial == "0" ? null : swords.GetValueOrDefault(serial, "个体：" + serial);
                    members.Add(id);
                }
                result.DutyMembers[job] = members;
            }
        foreach (var key in new[] { "Forge", "Repair" })
            if (result.WorkItems.TryGetValue(key, out var entries))
            {
                var numbered = entries.Select(x => (Entry: x, Number: SlotNumber(x.Label))).GroupBy(x => x.Number).ToDictionary(x => x.Key, x => x.Last().Entry);
                if (key == "Repair" && repairCapacity is { } count)
                    for (var number = 1; number <= count; number++)
                        numbered.TryAdd(number, new WorkEntry("工坊 " + number, null));
                result.WorkItems[key] = numbered.OrderBy(x => x.Key)
                    .Select(x => key == "Repair" ? x.Value with { Label = "工坊 " + x.Key } : x.Value).ToList();
            }
        foreach (var pair in teams)
        {
            var (status, finish, time) = pair.Value;
            var label = status switch { "0" => "未知", "1" => "待命", "2" => "远征中", "3" => "出阵中", _ => "未知状态（" + status + "）" };
            if (status == "2")
                label += "，" + maps.GetValueOrDefault(pair.Key, "地图未知") + Finish(finish == null ? null : new JValue(finish)).Replace("结束时间：", "", StringComparison.Ordinal);
            result.Teams[pair.Key] = new TeamEntry(status, finish);
            result.Values["Team" + pair.Key] = new Value(label, time);
        }
        return result;
    }

    private static int SlotNumber(string label) =>
        int.TryParse(Regex.Match(label, @"\d+").Value, out var number) ? number : int.MaxValue;

    private static string ExpeditionMap(string id) =>
        int.TryParse(id, out var value) && value is >= 1 and <= 20
            ? $"{(value - 1) / 4 + 1}-{(value - 1) % 4 + 1}"
            : "地图 ID：" + id;

    private static string Finish(JToken? token)
    {
        var value = token?.ToString();
        return string.IsNullOrWhiteSpace(value) || value == "0" || value.StartsWith("0000", StringComparison.Ordinal)
            ? "" : "，结束时间：" + (DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time.ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : value);
    }
}
