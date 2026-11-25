using System;
using System.IO;
using System.Linq;
using MFAAvalonia.Models;
using MFAAvalonia.Services;
using Xunit;
using MATR.GameLogReader;
using MATR.GameLogReaderSdk;

namespace Markdown.Avalonia.Html.Tests;

public class GameOverviewWarehouseSyncTests
{
    private static GameOverviewSnapshot Snapshot(string time = "2026-11-17 12:00:00")
    {
        var snapshot = new GameOverviewSnapshot { Account = "1" };
        snapshot.Values["Charcoal"] = new("1,040", time);
        return snapshot;
    }

    [Fact]
    public void 旧数据拒绝且不修改仓库()
    {
        var original = new WarehouseData();
        original.CoreResources["木炭"] = 2000;
        var result = GameOverviewWarehouseSync.Prepare(Snapshot(), original, new DateTime(2026, 11, 17, 13, 0, 0));
        Assert.Equal(GameOverviewWarehouseSync.State.Stale, result.Status);
        Assert.Equal(2000, original.CoreResources["木炭"]);
    }

    [Fact]
    public void 新数据合并且保留未知道具与原有历史()
    {
        var original = new WarehouseData();
        original.OtherItems["道具"] = 10;
        original.CoreResources["玉钢"] = 500;
        var result = GameOverviewWarehouseSync.Prepare(Snapshot(), original, null);
        Assert.Equal(GameOverviewWarehouseSync.State.Saved, result.Status);
        Assert.Equal(1040, result.Data!.CoreResources["木炭"]);
        Assert.Equal(500, result.Data.CoreResources["玉钢"]);
        Assert.Equal(10, result.Data.OtherItems["道具"]);
        Assert.Equal(new DateTime(2026, 11, 17, 12, 0, 0), Assert.Single(result.Data.ResourceHistory).RecordedAt);
        Assert.Empty(original.ResourceHistory);
    }

    [Fact]
    public void 部分旧字段不覆盖新仓库值()
    {
        var snapshot = Snapshot();
        snapshot.Values["Steel"] = new("100", "2026-11-17 10:00:00");
        var original = new WarehouseData();
        original.CoreResources["玉钢"] = 900;
        var result = GameOverviewWarehouseSync.Prepare(snapshot, original, new DateTime(2026, 11, 17, 11, 0, 0));
        Assert.Equal(900, result.Data!.CoreResources["玉钢"]);
        Assert.Equal(1040, result.Data.CoreResources["木炭"]);
    }

    [Fact]
    public void 重复值不追加历史且空数据不清零()
    {
        var data = new WarehouseData();
        data.CoreResources["木炭"] = 1040;
        Assert.Equal(GameOverviewWarehouseSync.State.Unchanged, GameOverviewWarehouseSync.Prepare(Snapshot(), data, null).Status);
        Assert.Equal(GameOverviewWarehouseSync.State.NoData, GameOverviewWarehouseSync.Prepare(new(), data, null).Status);
        Assert.Equal(1040, data.CoreResources["木炭"]);
    }

    [Fact]
    public void 保存写入真实仓库且重复快照幂等()
    {
        var root = Path.Combine(Path.GetTempPath(), "MATR-warehouse-extension-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new HonmaruDataStore(root, Path.Combine(root, "backup"));
            Assert.Equal(GameOverviewWarehouseSync.State.Saved, store.SyncExtension(Snapshot()).Status);
            Assert.Equal(1040, store.GetWarehouseData().CoreResources["木炭"]);
            Assert.Equal(GameOverviewWarehouseSync.State.Unchanged, store.SyncExtension(Snapshot()).Status);
            Assert.Single(store.GetWarehouseData().ResourceHistory);
            store.SaveWarehouseLastUpdatedAt("2026-11-17T13:00:00+08:00");
            // 模拟手动保存会清除扩展指纹。
            store.SaveWarehouseData(store.GetWarehouseData());
            Assert.Equal(GameOverviewWarehouseSync.State.Stale, store.SyncExtension(Snapshot()).Status);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories)) File.Delete(file);
                foreach (var directory in Directory.GetDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length)) Directory.Delete(directory);
                Directory.Delete(root);
            }
        }
    }
}
