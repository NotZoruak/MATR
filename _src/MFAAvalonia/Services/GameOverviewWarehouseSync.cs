using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MFAAvalonia.Models;

namespace MFAAvalonia.Services;

/// <summary>验证扩展资源事实并按快照采集时间合并到仓库，保留未覆盖的物品。</summary>
public static class GameOverviewWarehouseSync
{
    public enum State { Saved, Unchanged, Stale, NoData }
    public sealed record Result(State Status, WarehouseData? Data = null, DateTime? ObservedAt = null);
    private static readonly (string Source, string Target)[] Fields =
    [("Charcoal", "木炭"), ("Steel", "玉钢"), ("Coolant", "冷却材"), ("File", "砥石"), ("Bill", "委托符"), ("Accelerator", "加速符"), ("Money", "小判")];

    public static string Fingerprint(GameOverviewSnapshot snapshot)
    {
        var content = snapshot.Account
            + string.Concat(Fields.Select(pair => snapshot.Values.TryGetValue(pair.Source, out var value)
                ? "|" + pair.Source + ":" + value.Text + ":" + value.ObservedAt : ""))
            + (snapshot.HasInventorySnapshot ? "|Inventory:" : "")
            + string.Concat(snapshot.InventoryItems.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"|{pair.Key}:{pair.Value.Text}:{pair.Value.ObservedAt}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    }

    public static Result Prepare(GameOverviewSnapshot snapshot, WarehouseData existing, DateTime? lastUpdated, DateTimeOffset? snapshotAt = null)
    {
        var fields = new List<(string Key, int Value, DateTime Time)>();
        foreach (var pair in Fields)
            if (snapshot.Values.TryGetValue(pair.Source, out var value)
                && int.TryParse(value.Text, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var amount)
                && amount >= 0
                && DateTime.TryParseExact(value.ObservedAt, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                fields.Add((pair.Target, amount, time));
        if (snapshot.HasInventorySnapshot)
            foreach (var pair in snapshot.InventoryItems)
                if (!Fields.Any(field => field.Target == pair.Key)
                    && int.TryParse(pair.Value.Text, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var amount)
                    && amount >= 0
                    && DateTime.TryParseExact(pair.Value.ObservedAt, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                    fields.Add((pair.Key, amount, time));
        if (fields.Count == 0) return new(State.NoData);
        var cutoff = existing.ResourceHistory.Select(x => NormalizeTime(x.RecordedAt))
            .Concat(lastUpdated.HasValue ? new[] { NormalizeTime(lastUpdated.Value) } : Array.Empty<DateTime>())
            .DefaultIfEmpty(DateTime.MinValue).Max();
        var fresh = fields.Where(x => x.Time >= cutoff).ToList();
        if (fresh.Count == 0) return new(State.Stale);
        var freshCore = fresh.Where(x => Fields.Any(field => field.Target == x.Key)).ToList();
        var changedCore = freshCore.Where(x => !existing.CoreResources.TryGetValue(x.Key, out var old) || old != x.Value)
            .ToList();
        var changedItems = fresh.Where(x => !Fields.Any(field => field.Target == x.Key)
                && (!existing.OtherItems.TryGetValue(x.Key, out var old) || old != x.Value))
            .ToList();
        if (changedCore.Count == 0 && changedItems.Count == 0) return new(State.Unchanged);
        var snapshotTime = snapshotAt?.ToOffset(TimeSpan.FromHours(8)).DateTime ?? fresh.Max(x => x.Time);
        var merged = existing.Clone();
        foreach (var field in changedCore) merged.CoreResources[field.Key] = field.Value;
        foreach (var field in changedItems) merged.OtherItems[field.Key] = field.Value;
        if (changedCore.Count > 0)
            merged.ResourceHistory.Add(new WarehouseResourceSnapshot
            {
                RecordedAt = snapshotTime,
                Values = freshCore.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal)
            });
        return new(State.Saved, merged, snapshotTime);
    }

    private static DateTime NormalizeTime(DateTime value) => value.Kind == DateTimeKind.Unspecified
        ? value : new DateTimeOffset(value).ToOffset(TimeSpan.FromHours(8)).DateTime;
}
