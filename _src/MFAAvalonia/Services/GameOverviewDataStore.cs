using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace MFAAvalonia.Services;

/// <summary>在本丸目录中按账号保存扩展快照，不保存原始日志。</summary>
public sealed class GameOverviewDataStore(string configDirectory)
{
    public sealed record SavedSnapshot(int Version, DateTimeOffset CollectedAt, GameOverviewSnapshot Snapshot);

    /// <summary>加载最近保存的账号快照，不修改原采集时间。</summary>
    public SavedSnapshot? LoadLatest()
    {
        var directory = Path.Combine(configDirectory, "honmaru", "default", "snapshots");
        if (!Directory.Exists(directory)) return null;
        SavedSnapshot? latest = null;
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            if (!Regex.IsMatch(Path.GetFileName(file), @"^[0-9a-f]{64}\.json$")) continue;
            var saved = JsonConvert.DeserializeObject<SavedSnapshot>(File.ReadAllText(file));
            if (saved == null || saved.Version != 1 || saved.CollectedAt == default || saved.Snapshot == null
                || string.IsNullOrWhiteSpace(saved.Snapshot.Account)
                || Path.GetFileNameWithoutExtension(file) != Hash(saved.Snapshot.Account))
                throw new InvalidDataException("扩展快照文件无效。");
            if (latest == null || saved.CollectedAt > latest.CollectedAt) latest = saved;
        }
        return latest;
    }

    public void Save(GameOverviewSnapshot snapshot, DateTimeOffset? collectedAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot.Account);
        var directory = Path.Combine(configDirectory, "honmaru", "default", "snapshots");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Hash(snapshot.Account) + ".json");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var document = new SavedSnapshot(1, collectedAt ?? DateTimeOffset.Now, snapshot);
            File.WriteAllText(temporary, JsonConvert.SerializeObject(document, Formatting.Indented), new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
