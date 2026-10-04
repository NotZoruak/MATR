using MFAAvalonia.Extensions.MaaFW.Custom;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class FlowerEquipRemoveTeamActionTests
{
    [Theory]
    [InlineData(1, 377, 401)]
    [InlineData(2, 555, 402)]
    [InlineData(3, 730, 404)]
    [InlineData(4, 904, 401)]
    [InlineData(5, 381, 473)]
    public void 既有刷花部队动作应按目标部队返回装备解除位置(int team, int expectedX, int expectedY)
    {
        Assert.Equal((expectedX, expectedY), SelectFlowerTeamAction.GetEquipRemoveClickPoint(team));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void 既有刷花部队动作遇到无效编号时不返回装备解除位置(int team)
    {
        Assert.Null(SelectFlowerTeamAction.GetEquipRemoveClickPoint(team));
    }
}
