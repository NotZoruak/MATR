using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace MFAAvalonia.Services;

/// <summary>串行读取单个设备的日志，缓存仅驻留内存。</summary>
public sealed class GameOverviewReader
{
    public static GameOverviewReader Instance { get; } = new();
    private const string LogPath = "/data/data/com.youzu.djlw/files/userdata/HttpRequestCollect";
    private readonly SemaphoreSlim _gate = new(1);
    private string _adb = "";
    private string _serial = "";
    private bool _ownsRoot;
    private byte[] _log = [];
    private long _offset;
    private string _prefix = "";

    private GameOverviewReader()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            if (!_ownsRoot) return;
            try
            {
                var info = new ProcessStartInfo(_adb) { UseShellExecute = false, CreateNoWindow = true };
                foreach (var arg in new[] { "-s", _serial, "unroot" }) info.ArgumentList.Add(arg);
                using var process = Process.Start(info);
                process?.WaitForExit(2000);
            }
            catch { /* 退出时的权限清理不阻止进程结束。 */ }
        };
    }

    public async Task<GameOverviewLogParser.Snapshot> ReadAsync(string adb, string serial, bool keepRoot)
    {
        await _gate.WaitAsync();
        try
        {
            if (_adb != adb || _serial != serial)
            {
                await ReleaseRootAsync();
                _adb = adb;
                _serial = serial;
                _log = [];
                _offset = 0;
                _prefix = "";
            }
            var identity = await RunAsync("shell", "id");
            if (!identity.Contains("uid=0", StringComparison.Ordinal))
            {
                await RunAsync("root");
                _ownsRoot = true;
                await RunAsync("wait-for-device");
                identity = await RunAsync("shell", "id");
                if (!identity.Contains("uid=0", StringComparison.Ordinal))
                    throw new InvalidOperationException("模拟器未允许 ADB root，无法读取游戏日志。");
            }
            var sizeText = await RunAsync("shell", "wc -c < " + LogPath);
            if (!long.TryParse(sizeText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) || size < 0)
                throw new InvalidOperationException("无法确认游戏日志大小。");
            if (size > 32 * 1024 * 1024)
                throw new InvalidOperationException("游戏日志超过 32 MB，请重启游戏后再次同步。");
            var prefix = await RunAsync("shell", "head -c 256 " + LogPath);
            // 头部变化或文件缩短视为新会话，重建基线。
            var rebuild = size < _offset || prefix != _prefix;
            if (!rebuild && _offset > 0)
            {
                var count = (int)Math.Min(128, _offset);
                var boundary = await RunAsync("exec-out", "sh", "-c", "tail -c +" + (_offset - count + 1).ToString(CultureInfo.InvariantCulture) + " " + LogPath + " | head -c " + count + " | base64");
                rebuild = !Convert.FromBase64String(boundary).AsSpan().SequenceEqual(_log.AsSpan(_log.Length - count, count));
            }
            var offset = rebuild ? 0 : _offset;
            var encoded = await RunAsync("exec-out", "sh", "-c", "tail -c +" + (offset + 1).ToString(CultureInfo.InvariantCulture) + " " + LogPath + " | head -c " + (size - offset).ToString(CultureInfo.InvariantCulture) + " | base64");
            var content = Convert.FromBase64String(encoded);
            var previous = rebuild ? Array.Empty<byte>() : _log;
            var log = new byte[previous.Length + content.Length];
            previous.CopyTo(log, 0);
            content.CopyTo(log, previous.Length);
            var snapshot = await Task.Run(() => GameOverviewLogParser.Parse(System.Text.Encoding.UTF8.GetString(log)));
            if (snapshot.Responses == 0)
                throw new InvalidOperationException("日志中没有可用账号数据，请进入游戏本丸后再次同步。");
            _log = log;
            _offset = offset + content.Length;
            _prefix = prefix;
            return snapshot;
        }
        finally
        {
            // 自动读取期间复用本服务申请的权限，单次手动读取结束后恢复。
            try
            {
                if (!keepRoot) await ReleaseRootAsync();
            }
            finally { _gate.Release(); }
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try { await ReleaseRootAsync(); }
        finally { _gate.Release(); }
    }

    private async Task ReleaseRootAsync()
    {
        if (!_ownsRoot) return;
        try { await RunAsync("unroot"); }
        catch { /* 设备已断开时无法恢复，不能阻止应用退出。 */ }
        _ownsRoot = false;
    }

    private async Task<string> RunAsync(params string[] arguments)
    {
        var info = new ProcessStartInfo(_adb)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8
        };
        info.ArgumentList.Add("-s");
        info.ArgumentList.Add(_serial);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("无法启动 ADB。");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            var text = await output;
            await error;
            if (process.ExitCode != 0) throw new InvalidOperationException("ADB 读取失败，请确认设备连接、root 权限及游戏日志是否存在。");
            return text;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch (InvalidOperationException) { }
            throw new InvalidOperationException("ADB 读取超时，请检查模拟器连接。");
        }
    }
}
