using MFAAvalonia.Services;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class ForgeHarvestTests
{
    private static string Reply(string path, string data) => $"2026-11-23 23:49:24 S->C {path}?uid=1 status:200 data:{data}\n";

    [Theory]
    [InlineData("/forge/complete")]
    [InlineData("/forge/fast")]
    [InlineData("/forge/start")]
    public void 成功返回个体才记录获取且重复读取不重复(string path)
    {
        var log = Reply(path, """{"status":0,"sword_id":"81","serial_id":117384206}""");
        var parser = new GameOverviewLogParser.IncrementalParser();
        parser.Append(log);
        var record = Assert.Single(parser.Append(log).HarvestRecords);
        Assert.Equal("宗三左文字", record.Name);
        Assert.Equal("2026-11-23 23:49:24", record.Time);
        Assert.Equal("锻刀", record.Source);
    }

    [Fact]
    public void 履历失败响应和未领取锻刀均不生成收获()
    {
        var log = Reply("/record", """{"status":0,"history":[{"record_no":12,"sword_id":81,"created_at":1795448964,"charcoal":700,"steel":700,"coolant":700,"file":700}]}""") +
            Reply("/forge/complete", """{"status":1,"sword_id":81,"serial_id":1}""") +
            Reply("/forge/start", """{"status":0,"slot_no":1,"finished_at":"2026-11-24 10:00:00"}""");
        Assert.Empty(GameOverviewLogParser.Parse(log).HarvestRecords);
    }

    [Theory]
    [InlineData("/forge/completemultiple")]
    [InlineData("/forge/fastmultiple")]
    [InlineData("/forge/startmultiple")]
    public void 十连获取逐振记录(string path)
    {
        var result = GameOverviewLogParser.Parse(Reply(path, """{"status":0,"sword":{"1":{"sword_id":81,"serial_id":1},"2":{"sword_id":81,"serial_id":2}}}"""));
        Assert.Equal(2, result.HarvestRecords.Count);
    }
}
