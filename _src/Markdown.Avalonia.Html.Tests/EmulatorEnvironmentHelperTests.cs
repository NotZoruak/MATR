using MFAAvalonia.Helper;
using System;
using System.IO;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class EmulatorEnvironmentHelperTests
{
    [Theory]
    [InlineData("MuMuPlayer12", EmulatorKind.MuMu)]
    [InlineData("LDPlayer", EmulatorKind.LDPlayer)]
    [InlineData("雷电模拟器", EmulatorKind.LDPlayer)]
    [InlineData("Nox", EmulatorKind.Nox)]
    [InlineData("MEmu", EmulatorKind.MEmu)]
    [InlineData("逍遥模拟器", EmulatorKind.MEmu)]
    [InlineData("BlueStacks", EmulatorKind.BlueStacks)]
    [InlineData("Google Pixel 6", EmulatorKind.Unknown)]
    [InlineData("", EmulatorKind.Unknown)]
    public void 设备名应识别出模拟器类型(string deviceName, EmulatorKind expected)
    {
        Assert.Equal(expected, EmulatorEnvironmentHelper.DetectKindByDeviceName(deviceName));
    }

    [Theory]
    [InlineData("MuMuNxDevice", EmulatorKind.MuMu)]
    [InlineData("MuMuPlayer", EmulatorKind.MuMu)]
    [InlineData("dnplayer", EmulatorKind.LDPlayer)]
    [InlineData("Nox", EmulatorKind.Nox)]
    [InlineData("MEmu", EmulatorKind.MEmu)]
    [InlineData("HD-Player", EmulatorKind.BlueStacks)]
    [InlineData("explorer", EmulatorKind.Unknown)]
    public void 进程名应识别出模拟器类型(string processName, EmulatorKind expected)
    {
        Assert.Equal(expected, EmulatorEnvironmentHelper.DetectKindByProcessName(processName));
    }

    [Theory]
    [InlineData(@"D:\MuMuPlayer\nx_main\mumu-cli.exe", EmulatorKind.MuMu)]
    [InlineData(@"D:\MuMuPlayer\MuMuPlayer.exe", EmulatorKind.MuMu)]
    [InlineData(@"D:\LDPlayer\dnplayer.exe", EmulatorKind.LDPlayer)]
    [InlineData(@"D:\LDPlayer\ldconsole.exe", EmulatorKind.LDPlayer)]
    [InlineData(@"D:\Nox\bin\Nox.exe", EmulatorKind.Nox)]
    [InlineData(@"D:\Microvirt\MEmu\MEmu.exe", EmulatorKind.MEmu)]
    [InlineData(@"C:\Program Files\BlueStacks_nxt\HD-Player.exe", EmulatorKind.BlueStacks)]
    [InlineData(@"C:\Windows\notepad.exe", EmulatorKind.Unknown)]
    [InlineData("", EmulatorKind.Unknown)]
    public void 可执行文件路径应识别出模拟器类型(string executablePath, EmulatorKind expected)
    {
        Assert.Equal(expected, EmulatorEnvironmentHelper.DetectKindByExecutablePath(executablePath));
    }

    [Theory]
    [InlineData("-v 0", 0)]
    [InlineData("-v 3", 3)]
    [InlineData("--vmindex 1", 1)]
    [InlineData("--index=6", 6)]
    [InlineData("-index 4", 4)]
    [InlineData("-index:2", 2)]
    [InlineData("-i 5", 5)]
    [InlineData("--instance 7", 7)]
    [InlineData("-v 2 -other", 2)]
    public void 启动参数中的实例序号应能被解析(string arguments, int expected)
    {
        Assert.Equal(expected, EmulatorEnvironmentHelper.ResolveInstanceIndexFromArguments(arguments));
    }

    [Theory]
    [InlineData("")]
    [InlineData("-v")]
    [InlineData("-v abc")]
    [InlineData("-x 1")]
    [InlineData("-v 200")]
    [InlineData("-index")]
    public void 启动参数中没有实例序号时应返回空(string arguments)
    {
        Assert.Null(EmulatorEnvironmentHelper.ResolveInstanceIndexFromArguments(arguments));
    }

    [Theory]
    [InlineData(EmulatorKind.MuMu, "127.0.0.1:16384", 0)]
    [InlineData(EmulatorKind.MuMu, "127.0.0.1:16416", 1)]
    [InlineData(EmulatorKind.MuMu, "127.0.0.1:7555", 0)]
    [InlineData(EmulatorKind.MuMu, "127.0.0.1:5557", 1)]
    [InlineData(EmulatorKind.LDPlayer, "127.0.0.1:5555", 0)]
    [InlineData(EmulatorKind.LDPlayer, "127.0.0.1:5557", 1)]
    [InlineData(EmulatorKind.LDPlayer, "127.0.0.1:5559", 2)]
    [InlineData(EmulatorKind.LDPlayer, "emulator-5554", 0)]
    [InlineData(EmulatorKind.LDPlayer, "emulator-5556", 1)]
    [InlineData(EmulatorKind.Nox, "127.0.0.1:62001", 0)]
    [InlineData(EmulatorKind.Nox, "127.0.0.1:62025", 1)]
    [InlineData(EmulatorKind.Nox, "127.0.0.1:62049", 25)]
    [InlineData(EmulatorKind.MEmu, "127.0.0.1:21503", 0)]
    [InlineData(EmulatorKind.MEmu, "127.0.0.1:21513", 1)]
    public void 实例序号应按模拟器类型与端口反推(EmulatorKind kind, string adbSerial, int expected)
    {
        Assert.Equal(expected, EmulatorEnvironmentHelper.ResolveInstanceIndex(kind, adbSerial));
    }

    [Theory]
    [InlineData(EmulatorKind.MuMu, "127.0.0.1:16000")]
    [InlineData(EmulatorKind.MuMu, "127.0.0.1")]
    [InlineData(EmulatorKind.MuMu, "")]
    [InlineData(EmulatorKind.LDPlayer, "127.0.0.1:62001")]
    [InlineData(EmulatorKind.Nox, "127.0.0.1:5555")]
    [InlineData(EmulatorKind.BlueStacks, "127.0.0.1:5555")]
    [InlineData(EmulatorKind.Unknown, "127.0.0.1:5555")]
    public void 无法识别的端口不应给出实例序号(EmulatorKind kind, string adbSerial)
    {
        Assert.Null(EmulatorEnvironmentHelper.ResolveInstanceIndex(kind, adbSerial));
    }

    [Fact]
    public void 各模拟器的控制台命令模板应与其控制台语法一致()
    {
        var ldPlayer = EmulatorEnvironmentHelper.GetConsoleCommands(EmulatorKind.LDPlayer);
        Assert.NotNull(ldPlayer);
        Assert.Equal("quit --index {0}", ldPlayer!.Value.Shutdown);
        Assert.Equal("launch --index {0}", ldPlayer.Value.Launch);

        var nox = EmulatorEnvironmentHelper.GetConsoleCommands(EmulatorKind.Nox);
        Assert.NotNull(nox);
        Assert.Equal("quit -index:{0}", nox!.Value.Shutdown);
        Assert.Equal("launch -index:{0}", nox.Value.Launch);

        var memu = EmulatorEnvironmentHelper.GetConsoleCommands(EmulatorKind.MEmu);
        Assert.NotNull(memu);
        Assert.Equal("stop -i {0}", memu!.Value.Shutdown);
        Assert.Equal("start -i {0}", memu.Value.Launch);

        // MuMu 使用 control 子命令、蓝叠没有控制台，两者都不走命令模板
        Assert.Null(EmulatorEnvironmentHelper.GetConsoleCommands(EmulatorKind.MuMu));
        Assert.Null(EmulatorEnvironmentHelper.GetConsoleCommands(EmulatorKind.BlueStacks));
    }

    [Fact]
    public void 各模拟器应给出需要强制结束的进程名()
    {
        Assert.Contains("MuMuNxDevice", EmulatorEnvironmentHelper.GetProcessNames(EmulatorKind.MuMu));
        Assert.Contains("MuMuVMMHeadless", EmulatorEnvironmentHelper.GetProcessNames(EmulatorKind.MuMu));
        Assert.Contains("dnplayer", EmulatorEnvironmentHelper.GetProcessNames(EmulatorKind.LDPlayer));
        Assert.Contains("Nox", EmulatorEnvironmentHelper.GetProcessNames(EmulatorKind.Nox));
        Assert.Contains("MEmu", EmulatorEnvironmentHelper.GetProcessNames(EmulatorKind.MEmu));
        Assert.Contains("HD-Player", EmulatorEnvironmentHelper.GetProcessNames(EmulatorKind.BlueStacks));
        Assert.Empty(EmulatorEnvironmentHelper.GetProcessNames(EmulatorKind.Unknown));
    }

    [Fact]
    public void MuMu新版设备进程路径应上溯三级定位安装目录()
    {
        var root = CreateTempRoot();
        try
        {
            var deviceExe = Path.Combine(root, "nx_device", "12.0", "shell", "MuMuNxDevice.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(deviceExe)!);
            File.WriteAllText(deviceExe, "");

            Assert.Equal(root, EmulatorEnvironmentHelper.ResolveInstallRoot(EmulatorKind.MuMu, deviceExe));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void MuMu旧版主程序路径应上溯一级定位安装目录()
    {
        var root = CreateTempRoot();
        try
        {
            var legacyExe = Path.Combine(root, "shell", "MuMuPlayer.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(legacyExe)!);
            File.WriteAllText(legacyExe, "");

            Assert.Equal(legacyExe, EmulatorEnvironmentHelper.FindLaunchExecutable(EmulatorKind.MuMu, root, legacyExe));
            Assert.Null(EmulatorEnvironmentHelper.FindConsole(EmulatorKind.MuMu, root, legacyExe));
            Assert.Equal(root, EmulatorEnvironmentHelper.ResolveInstallRoot(EmulatorKind.MuMu, legacyExe));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void MuMu控制入口应优先mumu_cli再回退MuMuManager()
    {
        var root = CreateTempRoot();
        try
        {
            var mumuCli = Path.Combine(root, "nx_main", "mumu-cli.exe");
            var manager = Path.Combine(root, "shell", "MuMuManager.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(mumuCli)!);
            Directory.CreateDirectory(Path.GetDirectoryName(manager)!);
            File.WriteAllText(manager, "");

            Assert.Equal(manager, EmulatorEnvironmentHelper.FindConsole(EmulatorKind.MuMu, root, null));

            File.WriteAllText(mumuCli, "");
            Assert.Equal(mumuCli, EmulatorEnvironmentHelper.FindConsole(EmulatorKind.MuMu, root, null));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 雷电控制台与主程序应能从安装目录解析()
    {
        var root = CreateTempRoot();
        try
        {
            var player = Path.Combine(root, "dnplayer.exe");
            var console = Path.Combine(root, "ldconsole.exe");
            File.WriteAllText(player, "");
            File.WriteAllText(console, "");

            Assert.Equal(root, EmulatorEnvironmentHelper.ResolveInstallRoot(EmulatorKind.LDPlayer, player));
            Assert.Equal(console, EmulatorEnvironmentHelper.FindConsole(EmulatorKind.LDPlayer, root, player));
            Assert.Equal(player, EmulatorEnvironmentHelper.FindLaunchExecutable(EmulatorKind.LDPlayer, root, player));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 夜神控制台与主程序应能从bin目录解析()
    {
        var root = CreateTempRoot();
        try
        {
            var binDirectory = Path.Combine(root, "bin");
            Directory.CreateDirectory(binDirectory);
            var player = Path.Combine(binDirectory, "Nox.exe");
            var console = Path.Combine(binDirectory, "NoxConsole.exe");
            File.WriteAllText(player, "");
            File.WriteAllText(console, "");

            Assert.Equal(console, EmulatorEnvironmentHelper.FindConsole(EmulatorKind.Nox, root, player));
            Assert.Equal(player, EmulatorEnvironmentHelper.FindLaunchExecutable(EmulatorKind.Nox, root, player));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 逍遥控制台与主程序应能从安装目录解析()
    {
        var root = CreateTempRoot();
        try
        {
            var player = Path.Combine(root, "MEmu.exe");
            var console = Path.Combine(root, "memuc.exe");
            File.WriteAllText(player, "");
            File.WriteAllText(console, "");

            Assert.Equal(console, EmulatorEnvironmentHelper.FindConsole(EmulatorKind.MEmu, root, player));
            Assert.Equal(player, EmulatorEnvironmentHelper.FindLaunchExecutable(EmulatorKind.MEmu, root, player));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 蓝叠只有主程序没有控制台()
    {
        var root = CreateTempRoot();
        try
        {
            var player = Path.Combine(root, "HD-Player.exe");
            File.WriteAllText(player, "");

            Assert.Null(EmulatorEnvironmentHelper.FindConsole(EmulatorKind.BlueStacks, root, player));
            Assert.Equal(player, EmulatorEnvironmentHelper.FindLaunchExecutable(EmulatorKind.BlueStacks, root, player));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 未安装模拟器时不应解析出可执行文件()
    {
        var root = CreateTempRoot();
        try
        {
            Assert.Null(EmulatorEnvironmentHelper.FindConsole(EmulatorKind.MuMu, root, null));
            Assert.Null(EmulatorEnvironmentHelper.FindLaunchExecutable(EmulatorKind.MuMu, root, null));
            Assert.Null(EmulatorEnvironmentHelper.FindConsole(EmulatorKind.Unknown, root, null));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "matr-emulator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
