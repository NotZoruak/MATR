using System;
using System.IO;
using System.Text.Json.Nodes;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class ExpeditionSmartSchedulingTests
{
    [Fact]
    public void 本丸后勤刷新间隔应同时注入智能等待动作()
    {
        var interfaceRoot = JsonNode.Parse(File.ReadAllText(FindInterfacePath()))!.AsObject();
        var refreshInterval = interfaceRoot["option"]!["RefreshInterval"]!.AsObject();
        var smartWaitParam = refreshInterval["pipeline_override"]!["E_SmartWait"]!["action"]!["custom_action_param"]!.AsObject();

        Assert.Equal("SmartWaitAction", refreshInterval["pipeline_override"]!["E_SmartWait"]!["action"]!["custom_action"]!.GetValue<string>());
        Assert.Equal("{seconds}", smartWaitParam["interval"]!.GetValue<string>());
    }

    private static string FindInterfacePath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var interfacePath = Path.Combine(directory.FullName, "assets", "interface.json");
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
                return interfacePath;
        }

        throw new DirectoryNotFoundException("找不到 interface.json 所在的仓库根目录。");
    }
}
