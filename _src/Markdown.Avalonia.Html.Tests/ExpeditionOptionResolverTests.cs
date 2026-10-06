using MFAAvalonia.Extensions.MaaFW;
using System.Collections.Generic;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class ExpeditionOptionResolverTests
{
    [Fact]
    public void 应识别后勤任务中的远征选项是否开启()
    {
        var enabledLogistics = new MaaInterface.MaaInterfaceTask
        {
            Name = "后勤",
            Entry = "Expedition",
            Option = [new MaaInterface.MaaInterfaceSelectOption { Name = "远征", Index = 0 }]
        };
        var disabledLogistics = new MaaInterface.MaaInterfaceTask
        {
            Name = "后勤",
            Entry = "Expedition",
            Option = [new MaaInterface.MaaInterfaceSelectOption { Name = "远征", Index = 1 }]
        };

        Assert.True(ExpeditionOptionResolver.IsExpeditionEnabled([enabledLogistics]));
        Assert.False(ExpeditionOptionResolver.IsExpeditionEnabled([disabledLogistics]));
        Assert.False(ExpeditionOptionResolver.IsExpeditionEnabled([]));
    }

    [Fact]
    public void 应识别后勤任务中的内番选项是否开启()
    {
        var enabledLogistics = new MaaInterface.MaaInterfaceTask
        {
            Name = "后勤",
            Entry = "Expedition",
            Option = [new MaaInterface.MaaInterfaceSelectOption { Name = "内番", Index = 1 }]
        };
        var disabledLogistics = new MaaInterface.MaaInterfaceTask
        {
            Name = "后勤",
            Entry = "Expedition",
            Option = [new MaaInterface.MaaInterfaceSelectOption { Name = "内番", Index = 0 }]
        };

        Assert.True(ExpeditionOptionResolver.IsNaibanEnabled([enabledLogistics]));
        Assert.False(ExpeditionOptionResolver.IsNaibanEnabled([disabledLogistics]));
        Assert.False(ExpeditionOptionResolver.IsNaibanEnabled([]));
    }

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

    [Fact]
    public void 应从开启的远征子选项中读取部队地图配置()
    {
        var teamOption = new MaaInterface.MaaInterfaceSelectOption { Name = "部队一", Index = 5 };
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
                    SubOptions = [teamOption]
                }
            ]
        };

        Assert.Same(teamOption, ExpeditionOptionResolver.FindTeamMapOption(logistics, "部队一"));
        Assert.Equal(5, ExpeditionOptionResolver.FindTeamMapOption(logistics, "部队一")?.Index);
        Assert.Equal(["远征"], ExpeditionOptionResolver.GetSyncOptionNames(logistics, ["部队一", "部队二"]));
    }

    [Fact]
    public void 关闭远征时不应使用其下保存的部队地图配置()
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
                    SubOptions = [new MaaInterface.MaaInterfaceSelectOption { Name = "部队一", Index = 5 }]
                }
            ]
        };

        Assert.Empty(ExpeditionOptionResolver.GetExpeditionOptionsForSync(logistics));
        Assert.Null(ExpeditionOptionResolver.FindTeamMapOption(logistics, "部队一"));
    }

    [Fact]
    public void 应兼容旧版顶层部队地图配置()
    {
        var teamOption = new MaaInterface.MaaInterfaceSelectOption { Name = "部队一", Index = 5 };
        var logistics = new MaaInterface.MaaInterfaceTask
        {
            Name = "后勤",
            Entry = "Expedition",
            Option = [teamOption]
        };

        Assert.Same(teamOption, ExpeditionOptionResolver.FindTeamMapOption(logistics, "部队一"));
        Assert.Equal(["部队一", "部队二"], ExpeditionOptionResolver.GetSyncOptionNames(logistics, ["部队一", "部队二"]));
    }
}
