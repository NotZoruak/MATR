using MFAAvalonia.Extensions.MaaFW;
using System.Collections.Generic;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class ExpeditionSettingsMigrationTests
{
    [Fact]
    public void 应迁移长期计划状态和疲劳阈值()
    {
        var logistics = CreateLogisticsTask();
        var legacyOptions = new List<MaaInterface.MaaInterfaceSelectOption>
        {
            new()
            {
                Name = "长期远征计划",
                Index = 1,
                SubOptions =
                [
                    new()
                    {
                        Name = "疲劳阈值",
                        Data = new Dictionary<string, string?> { ["threshold"] = "88" }
                    }
                ]
            }
        };

        var migrated = ExpeditionOptionMigration.MigrateLegacyLongTermPlan(legacyOptions, logistics, hasCurrentExpeditionSettings: false);

        Assert.True(migrated);
        var expedition = logistics.Option!.Find(option => option.Name == "远征")!;
        var longPlan = expedition.SubOptions!.Find(option => option.Name == "长期远征计划")!;
        Assert.Equal(1, longPlan.Index);
        Assert.Equal("88", longPlan.SubOptions!.Find(option => option.Name == "疲劳阈值")!.Data!["threshold"]);
    }

    [Fact]
    public void 新远征设置已存在时不应被旧全局值覆盖()
    {
        var logistics = CreateLogisticsTask();
        logistics.Option![0].SubOptions =
        [
            new() { Name = "长期远征计划", Index = 0 }
        ];

        var migrated = ExpeditionOptionMigration.MigrateLegacyLongTermPlan(
            [new MaaInterface.MaaInterfaceSelectOption { Name = "长期远征计划", Index = 1 }],
            logistics,
            hasCurrentExpeditionSettings: true);

        Assert.False(migrated);
        Assert.Equal(0, logistics.Option[0].SubOptions![0].Index);
    }

    private static MaaInterface.MaaInterfaceTask CreateLogisticsTask() => new()
    {
        Name = "后勤",
        Entry = "Expedition",
        Option =
        [
            new()
            {
                Name = "远征",
                Index = 0,
                SubOptions =
                [
                    new() { Name = "长期远征计划", Index = 0, SubOptions = [] }
                ]
            }
        ]
    };
}
