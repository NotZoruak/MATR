using System.Reflection;
using System.Runtime.Loader;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using MATR.GameLogReaderSdk;
using MFAAvalonia.Helper;

namespace MFAAvalonia.Services;

/// <summary>按固定路径加载用户自行安装的 Windows x64 游戏日志读取器。</summary>
public static class GameLogReaderPluginService
{
    public const int CurrentApiVersion = 2;
    private static readonly Lazy<IGameLogReader?> Reader = new(LoadReader);

    public static bool IsAvailable => Reader.Value != null;

    public static IGameLogReader? GetReader() => Reader.Value;

    private static IGameLogReader? LoadReader()
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64) return null;

        var path = Path.Combine(AppPaths.InstallRoot, "plugins", "win-x64", "MATR.GameLogReader.dll");
        if (!File.Exists(path)) return null;

        try
        {
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path));
            var implementations = assembly.GetTypes()
                .Where(type => typeof(IGameLogReader).IsAssignableFrom(type) && type is { IsAbstract: false, IsInterface: false })
                .ToArray();
            if (implementations.Length != 1)
                throw new InvalidDataException("读取器必须且只能包含一个 IGameLogReader 实现。");

            var reader = Activator.CreateInstance(implementations[0]) as IGameLogReader
                ?? throw new InvalidDataException("无法创建游戏日志读取器。");
            if (reader.ApiVersion != CurrentApiVersion)
            {
                reader.DisposeAsync().AsTask().GetAwaiter().GetResult();
                throw new InvalidDataException($"读取器接口版本不兼容：需要 {CurrentApiVersion}，实际为 {reader.ApiVersion}。");
            }

            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try { reader.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
                catch (Exception exception) { LoggerHelper.Warning("释放游戏日志读取器失败：" + exception.GetType().Name); }
            };
            return reader;
        }
        catch (Exception exception)
        {
            LoggerHelper.Warning("游戏日志读取器未能加载：" + exception.GetType().Name + "，" + exception.Message);
            return null;
        }
    }
}
