using MFAAvalonia.Configuration;
using MFAAvalonia.Helper;
using MFAAvalonia.Models;
using MFAAvalonia.ViewModels.Pages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MFAAvalonia.Services;

/// <summary>管理本丸专用数据文件及首次迁移。</summary>
public sealed class HonmaruDataStore
{
    private const int ManifestVersion = 1;
    private static readonly string[] MigratedKeys =
    [
        ConfigurationKeys.WarehouseData,
        ConfigurationKeys.WarehouseLastUpdatedAt,
        ConfigurationKeys.SwordBookEntries,
        ConfigurationKeys.SwordBookLastUpdatedAt,
    ];

    private readonly object _syncRoot = new();
    private readonly string _configDirectory;
    private readonly string _backupDirectory;

    public HonmaruDataStore(string configDirectory, string backupDirectory)
    {
        _configDirectory = configDirectory;
        _backupDirectory = backupDirectory;
    }

    private string LegacyConfigPath => Path.Combine(_configDirectory, "config.json");
    private string HonmaruDirectory => Path.Combine(_configDirectory, "honmaru");
    private string DefaultDirectory => Path.Combine(HonmaruDirectory, "default");
    private string ManifestPath => Path.Combine(HonmaruDirectory, "manifest.json");
    private string WarehousePath => Path.Combine(DefaultDirectory, "warehouse.json");
    private string SwordBookPath => Path.Combine(DefaultDirectory, "swordbook.json");

    /// <summary>确保默认本丸数据目录已建立；失败时保留旧配置供调用方继续使用。</summary>
    public bool EnsureMigrated()
    {
        lock (_syncRoot)
        {
            if (HasCompletedMigration())
                return true;

            try
            {
                var legacyConfig = LoadLegacyConfig();
                var hasLegacyData = MigratedKeys.Any(legacyConfig.ContainsKey);
                if (hasLegacyData)
                    CreateLegacyBackup();

                var warehouseDocument = CreateWarehouseDocument(legacyConfig);
                var swordBookDocument = CreateSwordBookDocument(legacyConfig);
                WriteJsonAtomically(WarehousePath, warehouseDocument);
                WriteJsonAtomically(SwordBookPath, swordBookDocument);

                if (!CanReadBack(warehouseDocument, swordBookDocument))
                    throw new InvalidDataException("本丸数据文件校验失败。");

                WriteJsonAtomically(ManifestPath, new HonmaruManifest
                {
                    Version = ManifestVersion,
                    MigratedFrom = hasLegacyData ? "config.json" : null,
                    MigratedAt = DateTimeOffset.Now,
                });

                if (hasLegacyData)
                {
                    foreach (var key in MigratedKeys)
                        legacyConfig.Remove(key);
                    WriteTextAtomically(LegacyConfigPath, legacyConfig.ToString(Formatting.Indented));
                }

                CleanupOldBackups();
                return true;
            }
            catch (Exception ex)
            {
                LoggerHelper.Error("本丸数据迁移失败，继续使用旧配置。", ex);
                TryDeleteIncompleteData();
                return false;
            }
        }
    }

    /// <summary>读取仓库数据；迁移未完成时返回空数据。</summary>
    public WarehouseData GetWarehouseData()
    {
        lock (_syncRoot)
        {
            var document = LoadJson(WarehousePath, new WarehouseDocument());
            return document.Data?.Clone() ?? new WarehouseData();
        }
    }

    /// <summary>保存仓库数据，不修改旧配置文件。</summary>
    public void SaveWarehouseData(WarehouseData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        lock (_syncRoot)
        {
            if (!EnsureMigrated())
                return;
            var existing = LoadJson(WarehousePath, new WarehouseDocument());
            WriteJsonAtomically(WarehousePath, new WarehouseDocument
            {
                Data = data.Clone(),
                LastUpdatedAt = existing.LastUpdatedAt,
            });
        }
    }

    /// <summary>读取刀帐状态。</summary>
    public List<SwordBookPortraitState> GetSwordBookEntries()
    {
        lock (_syncRoot)
        {
            var document = LoadJson(SwordBookPath, new SwordBookDocument());
            return [.. document.Entries.Select(CloneSwordBookEntry)];
        }
    }

    /// <summary>保存刀帐状态，不修改旧配置文件。</summary>
    public void SaveSwordBookEntries(IEnumerable<SwordBookPortraitState> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        lock (_syncRoot)
        {
            if (!EnsureMigrated())
                return;
            var existing = LoadJson(SwordBookPath, new SwordBookDocument());
            WriteJsonAtomically(SwordBookPath, new SwordBookDocument
            {
                Entries = [.. entries.Select(CloneSwordBookEntry)],
                LastUpdatedAt = existing.LastUpdatedAt,
            });
        }
    }

    /// <summary>读取仓库最后更新时间。</summary>
    public string GetWarehouseLastUpdatedAt()
    {
        lock (_syncRoot)
            return LoadJson(WarehousePath, new WarehouseDocument()).LastUpdatedAt ?? string.Empty;
    }

    /// <summary>保存仓库最后更新时间。</summary>
    public void SaveWarehouseLastUpdatedAt(string value)
    {
        lock (_syncRoot)
        {
            if (!EnsureMigrated())
                return;
            var existing = LoadJson(WarehousePath, new WarehouseDocument());
            existing.LastUpdatedAt = value;
            WriteJsonAtomically(WarehousePath, existing);
        }
    }

    /// <summary>读取刀帐最后更新时间。</summary>
    public string GetSwordBookLastUpdatedAt()
    {
        lock (_syncRoot)
            return LoadJson(SwordBookPath, new SwordBookDocument()).LastUpdatedAt ?? string.Empty;
    }

    /// <summary>保存刀帐最后更新时间。</summary>
    public void SaveSwordBookLastUpdatedAt(string value)
    {
        lock (_syncRoot)
        {
            if (!EnsureMigrated())
                return;
            var existing = LoadJson(SwordBookPath, new SwordBookDocument());
            existing.LastUpdatedAt = value;
            WriteJsonAtomically(SwordBookPath, existing);
        }
    }

    /// <summary>按既有配置键读取迁移后的数据。</summary>
    public bool TryGetValue<T>(string key, out T value)
    {
        object? stored = key switch
        {
            ConfigurationKeys.WarehouseData => GetWarehouseData(),
            ConfigurationKeys.WarehouseLastUpdatedAt => GetWarehouseLastUpdatedAt(),
            ConfigurationKeys.SwordBookEntries => GetSwordBookEntries(),
            ConfigurationKeys.SwordBookLastUpdatedAt => GetSwordBookLastUpdatedAt(),
            _ => null,
        };

        if (stored is T typed)
        {
            value = typed;
            return true;
        }

        value = default!;
        return false;
    }

    /// <summary>按既有配置键保存迁移后的数据。</summary>
    public bool TrySetValue(string key, object value)
    {
        switch (key)
        {
            case ConfigurationKeys.WarehouseData when value is WarehouseData warehouse:
                SaveWarehouseData(warehouse);
                return true;
            case ConfigurationKeys.WarehouseLastUpdatedAt when value is string warehouseUpdatedAt:
                SaveWarehouseLastUpdatedAt(warehouseUpdatedAt);
                return true;
            case ConfigurationKeys.SwordBookEntries:
                try
                {
                    var entries = JsonConvert.DeserializeObject<List<SwordBookPortraitState>>(
                        JsonConvert.SerializeObject(value));
                    if (entries is null)
                        return false;
                    SaveSwordBookEntries(entries);
                    return true;
                }
                catch (JsonException)
                {
                    return false;
                }
            case ConfigurationKeys.SwordBookLastUpdatedAt when value is string swordBookUpdatedAt:
                SaveSwordBookLastUpdatedAt(swordBookUpdatedAt);
                return true;
            default:
                return false;
        }
    }

    private bool HasCompletedMigration() => File.Exists(ManifestPath)
        && File.Exists(WarehousePath)
        && File.Exists(SwordBookPath);

    private JObject LoadLegacyConfig()
    {
        if (!File.Exists(LegacyConfigPath))
            return new JObject();

        using var textReader = File.OpenText(LegacyConfigPath);
        using var jsonReader = new JsonTextReader(textReader)
        {
            DateParseHandling = DateParseHandling.None,
        };
        return JObject.Load(jsonReader);
    }

    private void CreateLegacyBackup()
    {
        Directory.CreateDirectory(_backupDirectory);
        var backupPath = Path.Combine(_backupDirectory, $"config-{DateTime.Now:yyyyMMddHHmmssfff}.json");
        File.Copy(LegacyConfigPath, backupPath, false);
    }

    private WarehouseDocument CreateWarehouseDocument(JObject config)
    {
        var warehouse = config[ConfigurationKeys.WarehouseData]?.ToObject<WarehouseData>() ?? new WarehouseData();
        return new WarehouseDocument
        {
            Data = warehouse,
            LastUpdatedAt = config[ConfigurationKeys.WarehouseLastUpdatedAt]?.Value<string>(),
        };
    }

    private SwordBookDocument CreateSwordBookDocument(JObject config)
    {
        var entries = config[ConfigurationKeys.SwordBookEntries]?.ToObject<List<SwordBookPortraitState>>() ?? [];
        return new SwordBookDocument
        {
            Entries = [.. entries.Select(CloneSwordBookEntry)],
            LastUpdatedAt = config[ConfigurationKeys.SwordBookLastUpdatedAt]?.Value<string>(),
        };
    }

    private bool CanReadBack(WarehouseDocument warehouse, SwordBookDocument swordBook)
    {
        var loadedWarehouse = LoadJson(WarehousePath, new WarehouseDocument());
        var loadedSwordBook = LoadJson(SwordBookPath, new SwordBookDocument());
        return loadedWarehouse.Data.CoreResources.Count == warehouse.Data.CoreResources.Count
            && loadedWarehouse.Data.OtherItems.Count == warehouse.Data.OtherItems.Count
            && loadedWarehouse.Data.ResourceHistory.Count == warehouse.Data.ResourceHistory.Count
            && loadedSwordBook.Entries.Count == swordBook.Entries.Count;
    }

    private void CleanupOldBackups()
    {
        if (!Directory.Exists(_backupDirectory))
            return;
        foreach (var backup in Directory.GetFiles(_backupDirectory, "config-*.json")
                     .OrderByDescending(File.GetCreationTimeUtc)
                     .Skip(3))
            File.Delete(backup);
    }

    private void TryDeleteIncompleteData()
    {
        if (File.Exists(ManifestPath))
            return;
        try
        {
            if (Directory.Exists(DefaultDirectory))
                Directory.Delete(DefaultDirectory, true);
        }
        catch (Exception cleanupException)
        {
            LoggerHelper.Warning($"清理迁移半成品失败：{cleanupException.Message}");
        }
    }

    private static T LoadJson<T>(string path, T defaultValue) where T : class
    {
        if (!File.Exists(path))
            return defaultValue;
        return JsonConvert.DeserializeObject<T>(File.ReadAllText(path)) ?? defaultValue;
    }

    private static void WriteJsonAtomically<T>(string path, T value)
        => WriteTextAtomically(path, JsonConvert.SerializeObject(value, Formatting.Indented));

    private static void WriteTextAtomically(string path, string content)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("数据文件路径缺少目录。");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, content);
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static SwordBookPortraitState CloneSwordBookEntry(SwordBookPortraitState entry) => new(
        entry.Number,
        entry.Owned,
        entry.Wounded,
        entry.TrueSword,
        entry.InnerCare,
        entry.Casual);

    private sealed class HonmaruManifest
    {
        public int Version { get; set; }
        public string? MigratedFrom { get; set; }
        public DateTimeOffset MigratedAt { get; set; }
    }

    private sealed class WarehouseDocument
    {
        public WarehouseData Data { get; set; } = new();
        public string? LastUpdatedAt { get; set; }
    }

    private sealed class SwordBookDocument
    {
        public List<SwordBookPortraitState> Entries { get; set; } = [];
        public string? LastUpdatedAt { get; set; }
    }
}

/// <summary>为既有配置读写提供默认本丸数据存储入口。</summary>
public static class HonmaruDataStoreService
{
    private static readonly object SyncRoot = new();
    private static HonmaruDataStore? _store;

    /// <summary>初始化默认本丸数据目录，并在需要时迁移旧配置。</summary>
    public static bool Initialize()
    {
        lock (SyncRoot)
        {
            if (_store is not null)
                return true;

            AppPaths.Initialize();
            var store = new HonmaruDataStore(AppPaths.ConfigDirectory, AppPaths.BackupDirectory);
            if (!store.EnsureMigrated())
                return false;

            _store = store;
            return true;
        }
    }

    /// <summary>尝试读取由本丸数据目录管理的配置项。</summary>
    public static bool TryGetValue<T>(string key, out T value)
    {
        lock (SyncRoot)
        {
            if (_store is not null)
                return _store.TryGetValue(key, out value);
        }

        value = default!;
        return false;
    }

    /// <summary>尝试保存由本丸数据目录管理的配置项。</summary>
    public static bool TrySetValue(string key, object value)
    {
        lock (SyncRoot)
            return _store?.TrySetValue(key, value) == true;
    }
}
