using MFAAvalonia.Services;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public sealed class GameEventNamesTests
{
    [Theory]
    [InlineData("活动 18505-1", "战术强化训练 巧克力大作战 · 初级")]
    [InlineData("活动 18505-2", "战术强化训练 巧克力大作战 · 中级")]
    [InlineData("活动 18505-3", "战术强化训练 巧克力大作战 · 上级")]
    [InlineData("活动 18505-9999", "战术强化训练 巧克力大作战 · 关卡 9999")]
    [InlineData("活动 999999-1", "活动 999999-1")]
    [InlineData("8-4", "8-4")]
    [InlineData("异去 1-1", "异去 1-1")]
    [InlineData(null, null)]
    public void 活动名称和关卡按复合编号查询且未知编号保持可读(string? raw, string? expected)
    {
        Assert.Equal(expected, GameEventNames.FormatMap(raw));
    }
}
