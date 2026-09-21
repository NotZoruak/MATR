using MFAAvalonia.Models;
using MFAAvalonia.Services;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class ForgeCalculatorWarehouseDataMapperTests
{
    [Fact]
    public void Read_从仓库数据映射计算器资源和道具()
    {
        var data = new WarehouseData
        {
            CoreResources = new()
            {
                ["木炭"] = 1000,
                ["玉钢"] = 2000,
                ["冷却材"] = 3000,
                ["砥石"] = 4000,
                ["委托符"] = 50,
                ["加速符"] = 60,
            },
            OtherItems = new()
            {
                ["御札·梅"] = 1,
                ["御札·竹"] = 2,
                ["御札·松"] = 3,
                ["御札·富士"] = 4,
            },
        };

        var result = ForgeCalculatorWarehouseDataMapper.Read(data);

        Assert.Equal(1000, result.Charcoal);
        Assert.Equal(2000, result.Steel);
        Assert.Equal(3000, result.Coolant);
        Assert.Equal(4000, result.Whetstone);
        Assert.Equal(1, result.Plum);
        Assert.Equal(2, result.Bamboo);
        Assert.Equal(3, result.Pine);
        Assert.Equal(4, result.Fuji);
        Assert.Equal(50, result.Permits);
        Assert.Equal(60, result.Speedups);
    }

    [Fact]
    public void Read_缺少御札时对应数量为零()
    {
        var data = new WarehouseData
        {
            OtherItems = new()
            {
                ["御札·松"] = 3,
            },
        };

        var result = ForgeCalculatorWarehouseDataMapper.Read(data);

        Assert.Equal(0, result.Plum);
        Assert.Equal(0, result.Bamboo);
        Assert.Equal(3, result.Pine);
        Assert.Equal(0, result.Fuji);
    }
}
