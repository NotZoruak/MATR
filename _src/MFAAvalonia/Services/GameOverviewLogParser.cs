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
    private static readonly Dictionary<string, string> TreasureFragmentNames = new()
    {
        ["6101"] = "曜变天目碎片",
        ["6103"] = "狮子螺钿鞍碎片",
        ["6105"] = "南蛮胴具足碎片",
        ["6107"] = "锷·月下梅树透图碎片",
        ["6109"] = "锷·双鹤图碎片",
        ["6111"] = "三所物·菊碎片",
        ["6113"] = "三所物·狮子碎片"
    };
    private static readonly Dictionary<string, string> SeasonalRewardNames = new()
    {
        ["4206"] = "天竺牡丹"
    };
    public sealed record Value(string Text, string ObservedAt);
    public sealed record TeamEntry(string Status, string? FinishedAt, string? ExpeditionMap = null, string? SortieMap = null, string? SortiePoint = null);
    public sealed record WorkEntry(string Label, string? FinishedAt);
    public sealed record HarvestEntry(string Id, string Kind, string Time, string Name, int Amount, string? SwordId, string? Map, string? Point, string Source);
    public sealed record DutyRecord(string Id, string Time, string DutyType, string SwordSerial, string SwordId, int UpType, int UpValue, bool Success, bool IsMaximum, JObject SwordData);
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
        public List<HarvestEntry> HarvestRecords { get; } = new();
        public List<DutyRecord> DutyRecords { get; } = new();
        public bool HasFullSwordPool { get; set; }
        public string SwordObservedAt { get; set; } = "";
        public int Responses { get; set; }
    }

    public static Snapshot Parse(string log) => new IncrementalParser().Append(log, flushIncompleteLine: true);

    public sealed class IncrementalParser
    {
        private Snapshot result = new();
        private readonly Dictionary<string, (string Status, string? Finish, string Time)> teams = new();
        private readonly Dictionary<string, string> maps = new();
        private readonly Dictionary<string, string> sortieMaps = new();
        private readonly Dictionary<string, string> pendingSortieMaps = new();
        private readonly Dictionary<string, string> sortiePoints = new();
        private string? pendingSortieTeam;
        private string? pendingSortieAccount;
        private string? activeSortieTeam;
        private readonly Dictionary<string, string> swords = new();
        private JObject? dutyDetails;
        private int? repairCapacity;
        private readonly Dictionary<string, JObject> requests = new();
        private string pendingLine = "";
        private int nextLineIndex;

        public Snapshot Append(string log, bool flushIncompleteLine = false)
        {
            var combined = pendingLine + log;
            if (flushIncompleteLine && combined.Length > 0 && combined[^1] != '\n')
                combined += '\n';
            var logLines = combined.Split('\n');
            var lineCount = logLines.Length - 1;
            if (flushIncompleteLine && logLines[^1].Length > 0) lineCount++;
            pendingLine = flushIncompleteLine ? "" : logLines[^1];
            for (var inputLineIndex = 0; inputLineIndex < lineCount; inputLineIndex++)
            {
                var line = logLines[inputLineIndex];
                var lineIndex = nextLineIndex++;
            var response = line.Contains("S->C", StringComparison.Ordinal);
            var request = line.Contains("C->S", StringComparison.Ordinal);
            if (!response && !request) continue;
            var requestRoute = Regex.Match(line, @"(/[a-zA-Z_]+(?:/[a-zA-Z_]+)*)(?:\?|\s)");
            var start = line.IndexOf('{');
            if (start < 0)
            {
                if (request && requestRoute.Success && requestRoute.Groups[1].Value is "/sally/sally" or "/sally/parallelpastsally")
                {
                    var values = RequestValues(line);
                    if (values.TryGetValue("party_no", out var party) && values.TryGetValue("episode_id", out var episode)
                        && values.TryGetValue("field_id", out var field) && int.TryParse(party, out var partyNo)
                        && partyNo is >= 1 and <= 5 && int.TryParse(episode, out var episodeNo) && episodeNo > 0
                        && int.TryParse(field, out var fieldNo) && fieldNo > 0)
                    {
                        pendingSortieTeam = partyNo.ToString(CultureInfo.InvariantCulture);
                        pendingSortieAccount = Regex.Match(line, @"[?&]uid=(\d+)").Groups[1].Value;
                        var isParallelPast = requestRoute.Groups[1].Value == "/sally/parallelpastsally";
                        pendingSortieMaps[pendingSortieTeam] = (isParallelPast ? "异去 " : "") + $"{episodeNo}-{fieldNo}";
                    }
                }
                continue;
            }
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
                if (path is "/sally/sally" or "/sally/parallelpastsally")
                {
                    var values = RequestValues(line);
                    if (values.TryGetValue("party_no", out var party) && values.TryGetValue("episode_id", out var episode)
                        && values.TryGetValue("field_id", out var field) && int.TryParse(party, out var partyNo)
                        && partyNo is >= 1 and <= 5 && int.TryParse(episode, out var episodeNo) && episodeNo > 0
                        && int.TryParse(field, out var fieldNo) && fieldNo > 0)
                    {
                        pendingSortieTeam = partyNo.ToString(CultureInfo.InvariantCulture);
                        pendingSortieAccount = account;
                        var isParallelPast = path == "/sally/parallelpastsally";
                        pendingSortieMaps[pendingSortieTeam] = (isParallelPast ? "异去 " : "") + $"{episodeNo}-{fieldNo}";
                    }
                }
                continue;
            }
            if (!Regex.IsMatch(line[..start], @"status:200\b") || data["status"]?.ToString() != "0")
            {
                if (path is "/sally/sally" or "/sally/parallelpastsally" && pendingSortieTeam != null)
                {
                    pendingSortieMaps.Remove(pendingSortieTeam);
                    pendingSortieTeam = null;
                }
                continue;
            }
            if (result.Account != account)
            {
                result = new Snapshot { Account = account };
                teams.Clear();
                maps.Clear();
                sortieMaps.Clear();
                if (pendingSortieAccount != account) pendingSortieMaps.Clear();
                sortiePoints.Clear();
                if (pendingSortieAccount != account)
                {
                    pendingSortieTeam = null;
                    pendingSortieAccount = null;
                    activeSortieTeam = null;
                }
                swords.Clear();
                dutyDetails = null;
                repairCapacity = null;
            }
            result.Responses++;
            var time = Regex.Match(line, @"\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}").Value;
            if (path is "/sally/sally" or "/sally/parallelpastsally" && pendingSortieTeam is { } dispatchedTeam)
            {
                if (pendingSortieMaps.Remove(dispatchedTeam, out var dispatchedMap))
                {
                    sortieMaps[dispatchedTeam] = dispatchedMap;
                }
                sortiePoints.Remove(dispatchedTeam);
                teams[dispatchedTeam] = ("3", null, time);
                activeSortieTeam = dispatchedTeam;
                pendingSortieTeam = null;
                pendingSortieAccount = null;
            }
            var currentSortieMap = sortieMaps.GetValueOrDefault(activeSortieTeam ?? "");
            var currentSortiePoint = sortiePoints.GetValueOrDefault(activeSortieTeam ?? "");
            if (activeSortieTeam is { } sortieTeam)
            {
                if (path is "/sally/forward" or "/sally/parallelpastforward"
                    && int.TryParse(data["square_id"]?.ToString(), out var squareId) && squareId > 0)
                {
                    sortiePoints[sortieTeam] = squareId.ToString(CultureInfo.InvariantCulture);
                    teams[sortieTeam] = ("3", null, time);
                }
                else if (path == "/home" || path == "/battle/battle"
                    && data["finish"] is JObject battleFinish && battleFinish.Value<bool?>("is_finish") == true)
                {
                    teams[sortieTeam] = ("1", null, time);
                    sortieMaps.Remove(sortieTeam);
                    sortiePoints.Remove(sortieTeam);
                    activeSortieTeam = null;
                }
            }
            var droppedSwordId = data["result"]?["get_sword_id"] ?? data["get_sword_id"];
            if (path is "/battle/battle" or "/battle/alloutbattle"
                && int.TryParse(droppedSwordId?.ToString(), out var getSwordId) && getSwordId > 0)
            {
                var dropMap = currentSortieMap;
                result.HarvestRecords.Add(new HarvestEntry($"{account}:{lineIndex}:battle:{getSwordId}", "sword", time, "刀剑 ID：" + getSwordId,
                    1, getSwordId.ToString(CultureInfo.InvariantCulture), dropMap,
                    currentSortiePoint, dropMap?.StartsWith("异去 ", StringComparison.Ordinal) == true ? "异去掉落" : "过去掉落"));
            }
            var itemRewards = data["item"] as JArray;
            var battleRewards = data["result"]?["reward"] as JArray;
            var battleDropRewards = data["result"]?["drop_reward"] as JArray;
            if (data["reward"] is JArray || itemRewards != null || battleRewards != null || battleDropRewards != null || data["season_reward"] is JArray)
            {
                var rewardSource = path switch
                {
                    "/mission/rewards" => "任务奖励",
                    "/startdash/rewards" or "/startdash/reward" => "活动奖励",
                    "/receive/get" => "活动/邮件奖励",
                    "/sally/forward" => "过去掉落",
                    "/sally/parallelpastforward" => "异去掉落",
                    "/battle/battle" => currentSortieMap?.StartsWith("异去 ", StringComparison.Ordinal) == true ? "异去掉落" : "过去掉落",
                    "/battle/alloutbattle" => "联队战奖励",
                    _ => "未知"
                };
                string? rewardMap = null;
                string? rewardPoint = null;
                if (path == "/conquest/complete")
                {
                    var partyNo = data["party_no"]?.ToString();
                    var fieldId = data["field_id"]
                        ?? data["summary"]?.Children().OfType<JObject>()
                            .FirstOrDefault(row => partyNo == null || row["party_no"]?.ToString() == partyNo)?["field_id"];
                    var map = fieldId != null ? ExpeditionMap(fieldId.ToString())
                        : maps.GetValueOrDefault(partyNo ?? "") ?? "地图未知";
                    var resultType = data["success"]?.ToString() == "2" ? "大成功" : "成功";
                    rewardSource = "远征奖励 · " + map + " · " + resultType;
                }
                else if (path is "/sally/forward" or "/sally/parallelpastforward")
                {
                    rewardMap = sortieMaps.GetValueOrDefault(activeSortieTeam ?? "");
                    rewardPoint = sortiePoints.GetValueOrDefault(activeSortieTeam ?? "");
                }
                else if (path is "/battle/battle" or "/battle/alloutbattle")
                {
                    rewardMap = currentSortieMap;
                    rewardPoint = currentSortiePoint;
                }
                AddResourceRewards(data["reward"] as JArray, time, lineIndex, rewardMap, rewardPoint, rewardSource);
                AddResourceRewards(itemRewards, time, lineIndex, rewardMap, rewardPoint, rewardSource);
                AddResourceRewards(battleRewards, time, lineIndex, rewardMap, rewardPoint, rewardSource, includeBattleBonus: true);
                AddResourceRewards(battleDropRewards, time, lineIndex, rewardMap, rewardPoint, rewardSource);
                AddResourceRewards(data["season_reward"] as JArray, time, lineIndex, rewardMap, rewardPoint,
                    rewardSource);
            }
            void AddResourceRewards(JArray? rewards, string observedAt, int sourceIndex, string? map, string? point, string source, bool includeBattleBonus = false)
            {
                if (rewards == null) return;
                var names = new Dictionary<string, string> { ["1"] = "委托符", ["2"] = "木炭", ["3"] = "玉钢", ["4"] = "冷却材", ["5"] = "砥石" };
                for (var rewardIndex = 0; rewardIndex < rewards.Count; rewardIndex++)
                {
                    if (rewards[rewardIndex] is not JObject reward) continue;
                    var itemType = reward["item_type"]?.ToString();
                    var itemId = reward["item_id"]?.ToString() ?? reward["consumable_id"]?.ToString()
                        ?? reward["sword_id"]?.ToString() ?? reward["artifact_id"]?.ToString();
                    var mappedItemName = itemId == null ? null
                        : TreasureFragmentNames.GetValueOrDefault(itemId) ?? SeasonalRewardNames.GetValueOrDefault(itemId);
                    var amountText = reward["item_num"]?.ToString() ?? reward["num"]?.ToString() ?? reward["amount"]?.ToString();
                    _ = int.TryParse(amountText, out var amount);
                    if (includeBattleBonus)
                    {
                        if (int.TryParse(reward["bonus"]?.ToString(), out var bonus)) amount += bonus;
                        if (int.TryParse(reward["bonus_monthcard_salary"]?.ToString(), out var monthlyBonus)) amount += monthlyBonus;
                    }
                    if (amount <= 0) continue;
                    var name = itemType switch
                    {
                        "4" => "小判",
                        "5" when itemId != null && names.TryGetValue(itemId, out var resourceName) => resourceName,
                        "1" when itemId == "8" => "加速符",
                        _ => null
                    } ?? (mappedItemName != null
                        ? mappedItemName
                        : itemId == null
                        ? $"未知物品（类型 {itemType ?? "未知"}）"
                        : $"未知物品（类型 {itemType ?? "未知"}，ID {itemId}）");
                    var isTreasureFragment = itemId != null && TreasureFragmentNames.ContainsKey(itemId);
                    var recordSource = isTreasureFragment && map?.StartsWith("异去 ", StringComparison.Ordinal) == true
                        ? "异去掉落"
                        : source;
                    var sourceKind = recordSource.Split(" · ", 2, StringSplitOptions.None)[0];
                    var id = $"{account}:{sourceIndex}:{rewardIndex}:{sourceKind}:{itemId ?? "未知"}:{itemType ?? "未知"}:{name}";
                    result.HarvestRecords.Add(new HarvestEntry(id, "resource", observedAt, name, amount, null, map, point, recordSource));
                }
            }
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
                        if (status == "1" && activeSortieTeam == number
                            && sortieMaps.GetValueOrDefault(number)?.StartsWith("异去 ", StringComparison.Ordinal) == true)
                            status = "3";
                        if (finish == null && teams.TryGetValue(number, out var previous) && previous.Status == status)
                            finish = previous.Finish;
                        teams[number] = (status, finish, time);
                        if (status != "2") maps.Remove(number);
                        if (status != "3")
                        {
                            sortieMaps.Remove(number);
                            sortiePoints.Remove(number);
                            if (activeSortieTeam == number) activeSortieTeam = null;
                        }
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
            if (path == "/push" && data["conquest"] is JArray expeditions)
            {
                maps.Clear();
                foreach (var expedition in expeditions.OfType<JObject>())
                    if (expedition["party_no"] is { } no && expedition["field_id"] is { } field)
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
                    if (data["param"] is JObject dutyParams && data["sword"] is JObject dutySwords)
                    {
                        var dutyKinds = new Dictionary<string, string>(StringComparer.Ordinal);
                        foreach (var (prefix, label) in new[] { ("horse", "饲马"), ("field", "耕作"), ("bout", "手合") })
                            for (var slot = 1; slot <= 2; slot++)
                                if (data[$"{prefix}_id{slot}"] is { } member) dutyKinds[member.ToString()] = label;
                        foreach (var pair in dutyParams.Properties())
                        {
                            if (pair.Value is not JObject growth || dutySwords[pair.Name] is not JObject sword
                                || !int.TryParse(growth["up_type"]?.ToString(), out var upType)
                                || !int.TryParse(growth["up_value"]?.ToString(), out var upValue)
                                || sword["sword_id"] == null) continue;
                            var serial = sword["serial_id"]?.ToString() ?? pair.Name;
                            var swordId = sword["sword_id"]!.ToString();
                            var timeKey = data["now"]?.ToString() ?? time;
                            result.DutyRecords.Add(new DutyRecord(
                                $"{account}:{timeKey}:{serial}", timeKey, dutyKinds.GetValueOrDefault(pair.Name, "内番"),
                                serial, swordId, upType, upValue, growth.Value<bool?>("success") ?? false,
                                growth.Value<bool?>("is_max") ?? false, (JObject)sword.DeepClone()));
                        }
                    }
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
            string? expeditionMap = null;
            string? sortieMap = null;
            string? sortiePoint = null;
            if (status == "2")
            {
                expeditionMap = maps.GetValueOrDefault(pair.Key);
                label += "，" + maps.GetValueOrDefault(pair.Key, "地图未知") + Finish(finish == null ? null : new JValue(finish)).Replace("结束时间：", "", StringComparison.Ordinal);
            }
            if (status == "3")
            {
                sortieMap = sortieMaps.GetValueOrDefault(pair.Key);
                sortiePoint = sortiePoints.GetValueOrDefault(pair.Key);
                label += "，" + (sortieMap ?? "地图未知");
                if (sortiePoint != null) label += "，点位 " + sortiePoint;
            }
            result.Teams[pair.Key] = new TeamEntry(status, finish, expeditionMap, sortieMap, sortiePoint);
            result.Values["Team" + pair.Key] = new Value(label, time);
        }
        return CopySnapshot();
        }

        private Snapshot CopySnapshot()
        {
            var copy = new Snapshot
            {
                Account = result.Account,
                HasFullSwordPool = result.HasFullSwordPool,
                SwordObservedAt = result.SwordObservedAt,
                Responses = result.Responses
            };
            foreach (var pair in result.Values) copy.Values[pair.Key] = pair.Value;
            foreach (var pair in result.WorkItems) copy.WorkItems[pair.Key] = [.. pair.Value];
            foreach (var pair in result.DutyMembers) copy.DutyMembers[pair.Key] = [.. pair.Value];
            foreach (var pair in result.Teams) copy.Teams[pair.Key] = pair.Value;
            foreach (var pair in result.HeldSwords) copy.HeldSwords[pair.Key] = (JObject)pair.Value.DeepClone();
            foreach (var pair in result.SwordTeams) copy.SwordTeams[pair.Key] = pair.Value;
            foreach (var pair in result.SwordTeamPositions) copy.SwordTeamPositions[pair.Key] = pair.Value;
            foreach (var pair in result.EquipmentKinds) copy.EquipmentKinds[pair.Key] = pair.Value;
            copy.HarvestRecords.AddRange(result.HarvestRecords);
            copy.DutyRecords.AddRange(result.DutyRecords.Select(record => record with { SwordData = (JObject)record.SwordData.DeepClone() }));
            return copy;
        }
    }

    private static int SlotNumber(string label) =>
        int.TryParse(Regex.Match(label, @"\d+").Value, out var number) ? number : int.MaxValue;

    private static Dictionary<string, string> RequestValues(string line)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(line, @"(?:[?&]|\b)(party_no|episode_id|field_id)=([^&\s,}]+)"))
            values[match.Groups[1].Value] = Uri.UnescapeDataString(match.Groups[2].Value);
        var form = Regex.Match(line, @"\bData:\[(?<data>[^]]*)\]");
        if (form.Success)
            foreach (var pair in form.Groups["data"].Value.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                if (parts.Length == 2 && parts[0] is "party_no" or "episode_id" or "field_id")
                    values[parts[0]] = Uri.UnescapeDataString(parts[1]);
            }
        var data = Regex.Match(line, @"(?:Data|data)\s*[:=]\s*(\[[^]]*\]|\{[^}]*\})");
        if (!data.Success) return values;
        try
        {
            var token = JToken.Parse(data.Groups[1].Value);
            if (token is JArray array)
                for (var index = 0; index + 1 < array.Count; index += 2)
                    if (array[index]?.ToString() is "party_no" or "episode_id" or "field_id")
                        values[array[index]!.ToString()] = array[index + 1]?.ToString() ?? "";
            if (token is JObject obj)
                foreach (var key in new[] { "party_no", "episode_id", "field_id" })
                    if (obj[key] != null) values[key] = obj[key]!.ToString();
        }
        catch (Newtonsoft.Json.JsonException) { }
        return values;
    }

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
