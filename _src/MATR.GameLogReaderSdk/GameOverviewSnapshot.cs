using Newtonsoft.Json.Linq;

namespace MATR.GameLogReaderSdk;

public sealed record GameOverviewValue(string Text, string ObservedAt);
public sealed record GameOverviewTeamEntry(string Status, string? FinishedAt, string? ExpeditionMap = null, string? SortieMap = null, string? SortiePoint = null);
public sealed record GameOverviewWorkEntry(string Label, string? FinishedAt);
public sealed record GameOverviewTrainingEntry(string SwordSerial, string? FinishedAt);
public sealed record GameOverviewHarvestEntry(string Id, string Kind, string Time, string Name, int Amount, string? SwordId, string? Map, string? Point, string Source);
public sealed record GameOverviewDutyRecord(string Id, string Time, string DutyType, string SwordSerial, string SwordId, int UpType, int UpValue, bool Success, bool IsMaximum, JObject SwordData);

/// <summary>读取器向宿主传递的本丸快照，不包含原始日志和认证信息。</summary>
public sealed class GameOverviewSnapshot
{
    public string Account { get; set; } = "";
    public Dictionary<string, GameOverviewValue> Values { get; } = new();
    public Dictionary<string, List<GameOverviewWorkEntry>> WorkItems { get; } = new();
    public List<GameOverviewTrainingEntry> TrainingItems { get; } = new();
    public Dictionary<string, List<string?>> DutyMembers { get; } = new();
    public Dictionary<string, List<string?>> DutyMemberSerials { get; } = new();
    public Dictionary<string, GameOverviewTeamEntry> Teams { get; } = new();
    public Dictionary<string, JObject> HeldSwords { get; } = new();
    public Dictionary<string, string> SwordTeams { get; } = new();
    public Dictionary<string, string> SwordTeamPositions { get; } = new();
    public Dictionary<string, string> EquipmentKinds { get; } = new();
    public Dictionary<string, GameOverviewValue> InventoryItems { get; } = new(StringComparer.Ordinal);
    public bool HasInventorySnapshot { get; set; }
    public DateTimeOffset? LatestResponseAt { get; set; }
    public List<GameOverviewHarvestEntry> HarvestRecords { get; } = new();
    public List<GameOverviewDutyRecord> DutyRecords { get; } = new();
    public bool HasFullSwordPool { get; set; }
    public string SwordObservedAt { get; set; } = "";
    public int Responses { get; set; }
}
