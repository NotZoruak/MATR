using MFAAvalonia.Extensions.MaaFW;
using System.Collections.Generic;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class ExpeditionOptionResolverTests
{
    [Fact]
    public void 应从开启的远征设置中读取长期计划()
    {
        var longTermPlan = new MaaInterface.MaaInterfaceSelectOption { Name = "长期远征计划", Index = 1 };
        var logistics = new MaaInterface.MaaInterfaceTask
        {
            Name = "后勤",
            Entry = "Expedition",
            Option =
            [
                new MaaInterface.MaaInterfaceSelectOption
                {
                    Name = "远征",
                    Index = 0,
                    SubOptions = [longTermPlan]
                }
            ]
        };

        var result = ExpeditionOptionResolver.FindEnabledLongTermPlanOption([logistics]);

        Assert.Same(longTermPlan, result);
    }

    [Fact]
    public void 关闭远征时不应读取长期计划()
    {
        var logistics = new MaaInterface.MaaInterfaceTask
        {
            Name = "后勤",
            Entry = "Expedition",
            Option =
            [
                new MaaInterface.MaaInterfaceSelectOption
                {
                    Name = "远征",
                    Index = 1,
                    SubOptions = [new MaaInterface.MaaInterfaceSelectOption { Name = "长期远征计划", Index = 1 }]
                }
            ]
        };

        Assert.Null(ExpeditionOptionResolver.FindEnabledLongTermPlanOption([logistics]));
    }
}
