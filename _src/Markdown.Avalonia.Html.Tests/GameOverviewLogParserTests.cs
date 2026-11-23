using MFAAvalonia.Services;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class GameOverviewLogParserTests
{
    [Theory]
    [InlineData("field_id")]
    [InlineData("event_field_id")]
    public void 活动出阵识别事件编号且单场结算与待命补丁不结束活动(string fieldKey)
    {
        var start = $"【2026-11-23 14:51:44】【C->S】[POST] https://example.test/sally/eventsally?uid=1 Data:[item_id=&event_id=18505&panel_id=851&party_no=1&{fieldKey}=1]\n" +
            "2026-11-23 14:51:44 S->C /sally/eventsally?uid=1 status:200 data:{\"status\":0}\n";
        var singleBattle = start + "2026-11-23 14:51:45 S->C /battle/battle?uid=1 status:200 data:{\"status\":0,\"finish\":{\"is_finish\":true,\"is_event_finish\":false}}\n" +
            "2026-11-23 14:51:46 S->C /party/list?uid=1 status:200 data:{\"status\":0,\"party\":{\"1\":{\"status\":1}}}\n";
        singleBattle += "2026-11-23 14:51:46 S->C /sally?uid=1 status:200 data:{\"status\":0,\"party\":{\"1\":{\"status\":1}}}\n";
        var active = GameOverviewLogParser.Parse(singleBattle);
        Assert.Equal("3", active.Teams["1"].Status);
        Assert.Equal("活动 18505-1", active.Teams["1"].SortieMap);
        var home = GameOverviewLogParser.Parse(singleBattle + "2026-11-23 14:51:47 S->C /home?uid=1 status:200 data:{\"status\":0}\n");
        Assert.Equal("1", home.Teams["1"].Status);
        var completed = GameOverviewLogParser.Parse(singleBattle + "2026-11-23 14:51:47 S->C /battle/battle?uid=1 status:200 data:{\"status\":0,\"finish\":{\"is_finish\":true,\"is_event_finish\":true}}\n");
        Assert.Equal("1", completed.Teams["1"].Status);
        var failed = GameOverviewLogParser.Parse(start.Replace("\"status\":0", "\"status\":1"));
        Assert.False(failed.Teams.ContainsKey("1"));
    }

    [Fact]
    public void 失败响应不能覆盖成功资源()
    {
        var log = Line("1", "0", "10") + "\n" + Line("1", "1", "999");
        Assert.Equal("10", GameOverviewLogParser.Parse(log).Values["Charcoal"].Text);
    }

    [Fact]
    public void 收获事件仅从成功战斗和明确资源奖励提取()
    {
        var log = "2026-11-18 10:00:00 C->S /sally/sally?uid=1&party_no=1&episode_id=8&field_id=4\n" +
            "2026-11-18 10:00:01 S->C /sally/sally?uid=1 readyState:4 status:200 data:{\"status\":0}\n" +
            "2026-11-18 10:00:02 S->C /sally/forward?uid=1 readyState:4 status:200 data:{\"status\":0,\"square_id\":21,\"reward\":[{\"item_type\":\"5\",\"item_id\":\"2\",\"item_num\":\"135\"}]}\n" +
            "2026-11-18 10:00:03 S->C /battle/battle?uid=1 readyState:4 status:200 data:{\"status\":0,\"get_sword_id\":49}\n" +
            "2026-11-18 10:00:04 S->C /battle/battle?uid=1 readyState:4 status:200 data:{\"status\":1,\"get_sword_id\":88}\n" +
            "2026-11-18 10:00:05 S->C /home?uid=1 readyState:4 status:200 data:{\"status\":0,\"resource\":{\"charcoal\":999}}";
        var records = GameOverviewLogParser.Parse(log).HarvestRecords;
        Assert.Collection(records,
            item => { Assert.Equal("resource", item.Kind); Assert.Equal("木炭", item.Name); Assert.Equal(135, item.Amount); Assert.Equal("8-4", item.Map); Assert.Equal("21", item.Point); },
            item => { Assert.Equal("sword", item.Kind); Assert.Equal("49", item.SwordId); Assert.Equal("8-4", item.Map); Assert.Equal("21", item.Point); });
    }

    [Fact]
    public void 切换账号清空上一账号字段()
    {
        var log = Line("1", "0", "10") + "\n" +
            "2026-11-17 10:01:00 S->C /home?uid=2 readyState:4 status:200 data: {\"status\":0}";
        var snapshot = GameOverviewLogParser.Parse(log);
        Assert.Equal("2", snapshot.Account);
        Assert.Empty(snapshot.Values);
    }

    [Fact]
    public void 不完整末行不影响已有数据()
    {
        var snapshot = GameOverviewLogParser.Parse(Line("1", "0", "0") + "\n2026-11-17 10:02:00 S->C /home?uid=1 status:200 data: {\"status\":");
        Assert.Equal("0", snapshot.Values["Charcoal"].Text);
        Assert.Equal("2026-11-17 10:00:00", snapshot.Values["Charcoal"].ObservedAt);
    }

    [Fact]
    public void 部队状态补丁不清空未涉及部队()
    {
        var log = "2026-11-17 10:00:00 S->C /sally?uid=1 status:200 data: {\"status\":0,\"party\":{\"1\":{\"status\":1},\"2\":{\"status\":2}}}\n" +
            "2026-11-17 10:01:00 S->C /home/situation?uid=1 status:200 data: {\"status\":0,\"party\":{\"1\":{\"status\":2}}}";
        var snapshot = GameOverviewLogParser.Parse(log);
        Assert.StartsWith("远征中", snapshot.Values["Team1"].Text);
        Assert.StartsWith("远征中", snapshot.Values["Team2"].Text);
    }

    [Fact]
    public void 加速符读取道具八且不混入其他道具()
    {
        var log = "2026-11-17 10:00:00 S->C /home?uid=1 status:200 data: {\"status\":0,\"item\":{\"8_0\":{\"consumable_id\":8,\"num\":1040},\"9_0\":{\"consumable_id\":9,\"num\":99}}}";
        Assert.Equal("1,040", GameOverviewLogParser.Parse(log).Values["Accelerator"].Text);
    }

    [Theory]
    [InlineData("2026-11-17 10:00:00", "完成", true)]
    [InlineData("2026-11-17 12:00:00", "结束时间：11-17 12:00:00", false)]
    [InlineData(null, "未使用", false)]
    [InlineData("0000-00-00 00:00:00", "未使用", false)]
    public void 工房完成状态按国服时区判断(string? finishedAt, string text, bool completed)
    {
        var now = new System.DateTimeOffset(2026, 11, 17, 10, 0, 0, System.TimeSpan.FromHours(8));
        var state = GameOverviewWorkStatus.Format(finishedAt, now);
        Assert.Equal(text, state.Text);
        Assert.Equal(completed, state.Completed);
    }

    [Fact]
    public void 工房保留各槽位结束时间供独立着色()
    {
        var log = "2026-11-17 10:00:00 S->C /forge?uid=1 status:200 data: {\"status\":0,\"forge\":{\"1\":{\"slot_no\":1,\"finished_at\":\"2026-11-17 09:00:00\"},\"2\":{\"slot_no\":2,\"finished_at\":\"2026-11-17 12:00:00\"}}}";
        Assert.Equal(2, GameOverviewLogParser.Parse(log).WorkItems["Forge"].Count);
    }

    [Fact]
    public void 远征目的地使用连续地图编号且保留结束时间补丁()
    {
        var log = "2026-11-17 10:00:00 S->C /party/list?uid=1 status:200 data: {\"status\":0,\"party\":{\"2\":{\"status\":2,\"finished_at\":\"2026-11-17 18:00:00\"}}}\n" +
            "2026-11-17 10:01:00 S->C /conquest?uid=1 status:200 data: {\"status\":0,\"summary\":{\"2\":{\"party_no\":2,\"field_id\":5}}}\n" +
            "2026-11-17 10:02:00 S->C /home/situation?uid=1 status:200 data: {\"status\":0,\"party\":{\"2\":{\"status\":2}}}";
        Assert.Equal("远征中，2-1，11-17 18:00:00", GameOverviewLogParser.Parse(log).Values["Team2"].Text);
    }

    [Fact]
    public void 锻刀按数值排序并补齐已开启的空闲修复工坊()
    {
        var log = "2026-11-17 10:00:00 S->C /home/situation?uid=1 status:200 data: {\"status\":0,\"repair_slot_max\":3,\"repair\":[],\"forge\":{\"1\":{\"slot_no\":1},\"3\":{\"slot_no\":3},\"2\":{\"slot_no\":2}}}";
        var snapshot = GameOverviewLogParser.Parse(log);
        Assert.Equal(new[] { "锻冶所 1", "锻冶所 2", "锻冶所 3" }, snapshot.WorkItems["Forge"].ConvertAll(x => x.Label));
        Assert.Equal(new[] { "工坊 1", "工坊 2", "工坊 3" }, snapshot.WorkItems["Repair"].ConvertAll(x => x.Label));
        Assert.All(snapshot.WorkItems["Repair"], x => Assert.Null(x.FinishedAt));
    }

    [Fact]
    public void 内番优先刀剑编号并可从后续刀剑记录解析个体编号()
    {
        var log = "2026-11-17 10:00:00 S->C /home?uid=1 status:200 data: {\"status\":0,\"duty\":{\"horse_id1\":111,\"horse_id2\":222,\"horse_sword_id1\":166,\"field_id1\":333}}\n" +
            "2026-11-17 10:01:00 S->C /sally?uid=1 status:200 data: {\"status\":0,\"sword_all\":{\"222\":{\"sword_id\":168},\"333\":{\"sword_id\":60}}}";
        var snapshot = GameOverviewLogParser.Parse(log);
        Assert.Equal(new[] { "166", "168" }, snapshot.DutyMembers["horse"]);
        Assert.Equal("60", snapshot.DutyMembers["field"][0]);
        Assert.All(snapshot.DutyMembers["bout"], x => Assert.Null(x));
    }

    [Fact]
    public void 派遣成功响应立即更新地图而无需等待打开远征页()
    {
        var log = "2026-11-17 10:00:00 S->C /conquest/start?uid=1 status:200 data: {\"status\":0,\"party\":{\"2\":{\"status\":2,\"finished_at\":\"2026-11-17 18:00:00\"}},\"summary\":{\"2\":{\"party_no\":2,\"field_id\":6}}}";
        Assert.Equal("远征中，2-2，11-17 18:00:00", GameOverviewLogParser.Parse(log).Values["Team2"].Text);
    }

    [Fact]
    public void 召回成功响应立即清除远征状态和地图()
    {
        var log = "2026-11-17 10:00:00 S->C /conquest/start?uid=1 status:200 data: {\"status\":0,\"party\":{\"2\":{\"status\":2}},\"summary\":{\"2\":{\"party_no\":2,\"field_id\":6}}}\n" +
            "2026-11-17 10:01:00 S->C /conquest/cancel?uid=1 status:200 data: {\"status\":0,\"party\":{\"2\":{\"status\":1,\"finished_at\":null}},\"summary\":{}}";
        Assert.Equal("待命", GameOverviewLogParser.Parse(log).Values["Team2"].Text);
    }

    [Fact]
    public void 部队五连续编号十六显示四之四()
    {
        var log = "2026-11-17 10:00:00 S->C /conquest/start?uid=1 status:200 data: {\"status\":0,\"party\":{\"5\":{\"status\":2}},\"summary\":{\"5\":{\"party_no\":5,\"field_id\":16}}}";
        Assert.Equal("远征中，4-4", GameOverviewLogParser.Parse(log).Values["Team5"].Text);
    }

    [Theory]
    [InlineData("2", "2026-11-17 10:00:00", true)]
    [InlineData("2", "2026-11-17 09:59:59", true)]
    [InlineData("2", "2026-11-17 10:00:01", false)]
    [InlineData("2", null, false)]
    [InlineData("2", "0000-00-00 00:00:00", false)]
    [InlineData("1", "2026-11-17 09:00:00", false)]
    public void 远征状态未变时到点显示已归来(string status, string? finish, bool returned)
    {
        var team = new GameOverviewLogParser.TeamEntry(status, finish);
        const string original = "远征中，2-1，11-17 10:00:00";
        var display = GameOverviewWorkStatus.FormatExpedition(team, original,
            new System.DateTimeOffset(2026, 11, 17, 10, 0, 0, System.TimeSpan.FromHours(8)));
        Assert.Equal(returned, display.Completed);
        Assert.Equal(returned ? "远征归来，2-1，11-17 10:00:00" : original, display.Text);
        Assert.Equal(status, team.Status);
    }

    [Fact]
    public void 登录响应提取审神者与属国并保留观察时间()
    {
        var log = "2026-11-17 10:00:00 S->C /login/start?uid=1 status:200 data: {\"status\":0,\"user_id\":1,\"name\":\"测试审神者\",\"server_name\":\"测试国\",\"castle_name\":\"测试本丸\"}";
        var snapshot = GameOverviewLogParser.Parse(log);
        Assert.Equal("测试审神者", snapshot.Values["PlayerName"].Text);
        Assert.Equal("测试国", snapshot.Values["Country"].Text);
        Assert.Equal("2026-11-17 10:00:00", snapshot.Values["Country"].ObservedAt);
    }

    private static string Line(string account, string status, string amount) =>
        "2026-11-17 10:00:00 S->C /home?uid=" + account + " readyState:4 status:200 data: {\"status\":" + status + ",\"resource\":{\"charcoal\":\"" + amount + "\"}}";
}
