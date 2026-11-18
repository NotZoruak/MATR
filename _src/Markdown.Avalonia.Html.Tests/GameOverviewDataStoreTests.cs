using System;
using System.IO;
using System.Linq;
using MFAAvalonia.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class GameOverviewDataStoreTests
{
    [Fact]
    public void 快照在本丸目录按账号隔离并保留字段时间()
    {
        var root = Path.Combine(Path.GetTempPath(), "MATR-overview-store-" + Guid.NewGuid().ToString("N"));
        var store = new GameOverviewDataStore(root);
        var first = new GameOverviewLogParser.Snapshot { Account = "1" };
        first.Values["Charcoal"] = new("100", "2026-11-17 10:00:00");
        store.Save(first);
        store.Save(first);
        store.Save(new() { Account = "2" });
        first.Values["Charcoal"] = new("200", "2026-11-17 11:00:00");
        first.WorkItems["Forge"] = [new("锻冶所 1", "2026-11-17 12:00:00")];
        first.DutyMembers["horse"] = ["166", "168"];
        first.HasFullSwordPool = true;
        first.HeldSwords["11"] = JObject.Parse("{\"sword_id\":3,\"level\":99}");
        first.Teams["2"] = new("2", "2026-11-17 10:00:00");
        store.Save(first);
        var restored = store.LoadLatest();
        Assert.NotNull(restored);
        Assert.Equal("1", restored.Snapshot.Account);
        Assert.Equal("200", restored.Snapshot.Values["Charcoal"].Text);
        Assert.Equal("2026-11-17 11:00:00", restored.Snapshot.Values["Charcoal"].ObservedAt);
        Assert.Single(restored.Snapshot.WorkItems["Forge"]);
        Assert.True(restored.Snapshot.HasFullSwordPool);
        Assert.Equal("99", restored.Snapshot.HeldSwords["11"]["level"]?.ToString());
        Assert.Equal(new[] { "166", "168" }, restored.Snapshot.DutyMembers["horse"]);
        Assert.True(restored.CollectedAt <= DateTimeOffset.Now);
        Assert.True(GameOverviewWorkStatus.FormatExpedition(restored.Snapshot.Teams["2"], "远征中，2-1",
            new DateTimeOffset(2026, 11, 17, 11, 0, 0, TimeSpan.FromHours(8))).Completed);
        var directory = Path.Combine(root, "honmaru", "extension");
        var files = Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories);
        Assert.Equal(2, files.Length);
        Assert.Empty(Directory.GetDirectories(directory));
        var document = files.Select(path => JObject.Parse(File.ReadAllText(path)))
            .First(x => x["Snapshot"]?["Values"]?["Charcoal"] != null);
        Assert.Equal("2026-11-17 11:00:00", document["Snapshot"]?["Values"]?["Charcoal"]?["ObservedAt"]?.ToString());
        Assert.Empty(Directory.GetFiles(directory, "*.tmp", SearchOption.AllDirectories));
        foreach (var file in files) File.Delete(file);
        Directory.Delete(directory);
        Directory.Delete(Path.Combine(root, "honmaru"));
        Directory.Delete(root);
    }
}
