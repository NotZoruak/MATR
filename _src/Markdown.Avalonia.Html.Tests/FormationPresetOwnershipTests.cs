using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Models;
using MFAAvalonia.ViewModels.Pages;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class FormationPresetOwnershipTests
{
    [Fact]
    public void 不同面板读写同编号预设时应始终使用各自所属实例()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MATR-编队归属-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var first = CreateConfiguration(Path.Combine(directory, "实例一"), "二队方案", 2);
            var second = CreateConfiguration(Path.Combine(directory, "实例二"), "四队方案", 4);
            var firstPanel = CreateGenerator(first);
            var secondPanel = CreateGenerator(second);

            var firstPresets = Load(firstPanel);
            Assert.Equal("二队方案", Assert.Single(firstPresets).Name);
            Assert.Equal(2, firstPresets[0].Team);
            Assert.Equal(6, firstPresets[0].Slots.Count);
            Assert.Equal("四队方案", Assert.Single(Load(secondPanel)).Name);

            // 另一面板读取后，原面板的保存回调仍必须写回原实例。
            firstPresets[0].Name = "重新保存的二队方案";
            typeof(TaskOptionGenerator).GetMethod("SaveFormationPresets", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(firstPanel, [firstPresets]);

            Assert.Equal("重新保存的二队方案", Assert.Single(Load(firstPanel)).Name);
            Assert.Contains("重新保存的二队方案", File.ReadAllText(first.GetConfigFilePath()));
            Assert.False(File.Exists(second.GetConfigFilePath()));
            var unchanged = Assert.Single(Load(secondPanel));
            Assert.Equal("四队方案", unchanged.Name);
            Assert.Equal(4, unchanged.Team);
        }
        finally
        {
            // 只清理本测试创建的临时文件。
            foreach (var file in Directory.EnumerateFiles(directory))
                File.Delete(file);
            Directory.Delete(directory);
        }
    }

    private static InstanceConfiguration CreateConfiguration(string path, string name, int team)
    {
        // 绕过应用初始化，避免加载或修改真实用户配置。
        var configuration = (InstanceConfiguration)RuntimeHelpers.GetUninitializedObject(typeof(InstanceConfiguration));
        SetField(configuration, "_instanceId", path);
        SetField(configuration, "_config", new Dictionary<string, object>
        {
            [ConfigurationKeys.FormationPresets] = new List<FormationPreset>
            {
                new() { Id = 3, Name = name, Team = team }
            }
        });
        return configuration;
    }

    private static TaskOptionGenerator CreateGenerator(InstanceConfiguration configuration)
    {
        var processor = (MaaProcessor)RuntimeHelpers.GetUninitializedObject(typeof(MaaProcessor));
        SetField(processor, "<InstanceConfiguration>k__BackingField", configuration);
        var viewModel = (TaskQueueViewModel)RuntimeHelpers.GetUninitializedObject(typeof(TaskQueueViewModel));
        SetField(viewModel, "_processorField", processor);
        return new TaskOptionGenerator(viewModel, () => { });
    }

    private static List<FormationPreset> Load(TaskOptionGenerator generator)
        => (List<FormationPreset>)typeof(TaskOptionGenerator)
            .GetMethod("LoadFormationPresets", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(generator, null)!;

    private static void SetField(object target, string name, object value)
        => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
}
