using MFAAvalonia.Configuration;
using MFAAvalonia.Models;
using MFAAvalonia.Services;
using MFAAvalonia.ViewModels.Pages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public sealed class HonmaruDataStoreTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(Path.GetTempPath(), $"matr-honmaru-test-{Guid.NewGuid():N}");

    [Fact]
    public void EnsureMigrated_迁移仓库和刀帐并保留无关配置()
    {
        var configDirectory = Path.Combine(_rootDirectory, "config");
        var backupDirectory = Path.Combine(_rootDirectory, "backup");
        Directory.CreateDirectory(configDirectory);
        var configPath = Path.Combine(configDirectory, "config.json");
        File.WriteAllText(configPath, JsonConvert.SerializeObject(CreateLegacyConfig()));
        var store = new HonmaruDataStore(configDirectory, backupDirectory);

        var migrated = store.EnsureMigrated();

        Assert.True(migrated);
        Assert.True(File.Exists(Path.Combine(configDirectory, "honmaru", "manifest.json")));
        Assert.True(File.Exists(Path.Combine(configDirectory, "honmaru", "default", "warehouse.json")));
        Assert.True(File.Exists(Path.Combine(configDirectory, "honmaru", "default", "swordbook.json")));
        Assert.Single(Directory.GetFiles(backupDirectory, "config-*.json"));

        var warehouse = store.GetWarehouseData();
        Assert.Equal(1200, warehouse.CoreResources["木炭"]);
        Assert.Equal(8, warehouse.OtherItems["御札·梅"]);
        Assert.Single(warehouse.ResourceHistory);

        var swordBook = store.GetSwordBookEntries();
        var entry = Assert.Single(swordBook);
        Assert.Equal("1", entry.Number);
        Assert.True(entry.Owned);
        Assert.True(entry.TrueSword);
        Assert.Equal("2026-10-05T10:00:00.0000000+08:00", store.GetWarehouseLastUpdatedAt());
        Assert.Equal("2026-10-05T10:05:00.0000000+08:00", store.GetSwordBookLastUpdatedAt());

        var savedConfig = JObject.Parse(File.ReadAllText(configPath));
        Assert.Equal("Blue", savedConfig["ColorTheme"]?.Value<string>());
        Assert.Null(savedConfig[ConfigurationKeys.WarehouseData]);
        Assert.Null(savedConfig[ConfigurationKeys.WarehouseLastUpdatedAt]);
        Assert.Null(savedConfig[ConfigurationKeys.SwordBookEntries]);
        Assert.Null(savedConfig[ConfigurationKeys.SwordBookLastUpdatedAt]);
    }

    [Fact]
    public void EnsureMigrated_已迁移时不覆盖现有本丸数据()
    {
        var configDirectory = Path.Combine(_rootDirectory, "config");
        var backupDirectory = Path.Combine(_rootDirectory, "backup");
        Directory.CreateDirectory(configDirectory);
        var configPath = Path.Combine(configDirectory, "config.json");
        File.WriteAllText(configPath, JsonConvert.SerializeObject(CreateLegacyConfig()));
        var store = new HonmaruDataStore(configDirectory, backupDirectory);
        Assert.True(store.EnsureMigrated());

        store.SaveWarehouseData(new WarehouseData
        {
            CoreResources = new Dictionary<string, int> { ["木炭"] = 9999 },
        });

        Assert.True(store.EnsureMigrated());

        Assert.Equal(9999, store.GetWarehouseData().CoreResources["木炭"]);
        Assert.Single(Directory.GetFiles(backupDirectory, "config-*.json"));
    }

    [Fact]
    public void TrySetValue_迁移字段只写入本丸目录()
    {
        var configDirectory = Path.Combine(_rootDirectory, "config");
        var backupDirectory = Path.Combine(_rootDirectory, "backup");
        Directory.CreateDirectory(configDirectory);
        var configPath = Path.Combine(configDirectory, "config.json");
        File.WriteAllText(configPath, JsonConvert.SerializeObject(CreateLegacyConfig()));
        var store = new HonmaruDataStore(configDirectory, backupDirectory);
        Assert.True(store.EnsureMigrated());

        Assert.True(store.TrySetValue(
            ConfigurationKeys.WarehouseData,
            new WarehouseData { CoreResources = new Dictionary<string, int> { ["玉钢"] = 4321 } }));
        Assert.True(store.TrySetValue(ConfigurationKeys.WarehouseLastUpdatedAt, "2026-10-05T12:00:00.0000000+08:00"));
        Assert.True(store.TryGetValue<WarehouseData>(ConfigurationKeys.WarehouseData, out var warehouse));
        Assert.True(store.TryGetValue<string>(ConfigurationKeys.WarehouseLastUpdatedAt, out var updatedAt));

        Assert.Equal(4321, warehouse.CoreResources["玉钢"]);
        Assert.Equal("2026-10-05T12:00:00.0000000+08:00", updatedAt);
        var savedConfig = JObject.Parse(File.ReadAllText(configPath));
        Assert.Null(savedConfig[ConfigurationKeys.WarehouseData]);
        Assert.Null(savedConfig[ConfigurationKeys.WarehouseLastUpdatedAt]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
            Directory.Delete(_rootDirectory, true);
    }

    private static JObject CreateLegacyConfig() => new()
    {
        ["ColorTheme"] = "Blue",
        [ConfigurationKeys.WarehouseData] = JToken.FromObject(new WarehouseData
        {
            CoreResources = new Dictionary<string, int> { ["木炭"] = 1200 },
            OtherItems = new Dictionary<string, int> { ["御札·梅"] = 8 },
            ResourceHistory =
            [
                new WarehouseResourceSnapshot
                {
                    RecordedAt = new DateTime(2026, 10, 5, 9, 0, 0, DateTimeKind.Local),
                    Values = new Dictionary<string, int> { ["木炭"] = 1100 },
                },
            ],
        }),
        [ConfigurationKeys.WarehouseLastUpdatedAt] = "2026-10-05T10:00:00.0000000+08:00",
        [ConfigurationKeys.SwordBookEntries] = JToken.FromObject(new List<SwordBookPortraitState>
        {
            new("1", true, false, true, false, false),
        }),
        [ConfigurationKeys.SwordBookLastUpdatedAt] = "2026-10-05T10:05:00.0000000+08:00",
    };
}
