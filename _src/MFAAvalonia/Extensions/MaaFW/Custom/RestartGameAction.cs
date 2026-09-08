using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Helper;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

public class RestartGameAction : IMaaCustomAction
{
    public string Name { get; set; } = nameof(RestartGameAction);

    private string? _adbPath;
    private string? _adbSerial;
    private string? _mumuPath;
    private int _mumuIndex;
    private string? _mumuCliExe;         // mumu-cli.exe 完整路径（MuMu 12+ 通过 CLI 控制）
    private string? _mumuLegacyExe;      // 旧版 MuMuPlayer.exe 完整路径
    private string? _mumuProcessName;    // 旧版要杀的进程名
    private bool _isMuMu12;              // 是否为 MuMu 12+（支持 CLI）

    private CancellationToken _recoveryToken;

    private void WaitForRecovery(int milliseconds)
    {
        if (_recoveryToken.WaitHandle.WaitOne(milliseconds))
            _recoveryToken.ThrowIfCancellationRequested();
    }

    private void EnsureAdbInfo(MaaProcessor? owner = null)
    {
        if (_adbPath != null) return;

        var processor = owner ?? MaaProcessorManager.Instance.Current;
        if (processor != null)
        {
            _adbPath = processor.Config.AdbDevice.AdbPath;
            _adbSerial = processor.Config.AdbDevice.AdbSerial;

            var configStr = processor.Config.AdbDevice.Config;
            if (!string.IsNullOrWhiteSpace(configStr))
            {
                try
                {
                    using var doc = JsonDocument.Parse(configStr);
                    if (doc.RootElement.TryGetProperty("extras", out var extras) &&
                        extras.TryGetProperty("mumu", out var mumu))
                    {
                        if (mumu.TryGetProperty("path", out var path))
                            _mumuPath = path.GetString();
                        if (mumu.TryGetProperty("index", out var index))
                            _mumuIndex = index.GetInt32();
                    }
                }
                catch { }
            }
        }
        _adbPath ??= "adb";
        _adbSerial ??= "";
        _mumuPath ??= "";

        DetectMuMuEnvironment();
    }

    /// <summary>
    /// 探测 MuMu 环境：MuMu 12+ 优先用 CLI（mumu-cli.exe），旧版回退到 MuMuPlayer.exe
    /// </summary>
    private void DetectMuMuEnvironment()
    {
        if (string.IsNullOrWhiteSpace(_mumuPath) || !Directory.Exists(_mumuPath))
            return;

        // MuMu 12+：使用 mumu-cli.exe 控制实例重启
        var cliExe = Path.Combine(_mumuPath, "nx_main", "mumu-cli.exe");
        if (File.Exists(cliExe))
        {
            _mumuCliExe = cliExe;
            _isMuMu12 = true;
            // 顺带探测旧版主程序，CLI 方式失败时回退用
            var fallbackLegacyExe = Path.Combine(_mumuPath, "MuMuPlayer.exe");
            if (File.Exists(fallbackLegacyExe))
            {
                _mumuLegacyExe = fallbackLegacyExe;
                _mumuProcessName = "MuMuPlayer";
            }
            return;
        }

        // 旧版 MuMu：杀 MuMuPlayer.exe 进程后重新启动
        var legacyExe = Path.Combine(_mumuPath, "MuMuPlayer.exe");
        if (File.Exists(legacyExe))
        {
            _mumuLegacyExe = legacyExe;
            _mumuProcessName = "MuMuPlayer";
            _isMuMu12 = false;
            return;
        }
    }

    private static string GetPackageName()
    {
        var globalOpts = MaaProcessor.Interface?.GlobalSelectOptions;
        var clientTypeOption = globalOpts?.FirstOrDefault(o => o.Name == "客户端类型");
        var customPackageOption = clientTypeOption?.SubOptions?.FirstOrDefault(o => o.Name == "其它客户端包名");
        string? customPackageName = null;
        if (customPackageOption?.Data != null)
            customPackageOption.Data.TryGetValue("package_name", out customPackageName);
        var clientType = clientTypeOption?.Index == 1 ? ClientPackageType.Other : ClientPackageType.Official;
        return ClientPackageSettings.ResolvePackageName(clientType, customPackageName);
    }

    private bool RestartEmulator()
    {
        _recoveryToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            LoggerHelper.Warning("[RestartGameAction] 当前平台不支持 MuMu Windows 重启命令");
            return false;
        }
        if (_isMuMu12)
        {
            return RestartEmulatorViaCli();
        }

        return RestartEmulatorLegacy();
    }

    /// <summary>
    /// MuMu 12+：通过 mumu-cli.exe control restart 重启实例，失败时回退旧版方式
    /// </summary>
    private bool RestartEmulatorViaCli()
    {
        _recoveryToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(_mumuCliExe))
        {
            LoggerHelper.Info("[RestartGameAction] 未找到 mumu-cli.exe，跳过模拟器重启");
            return false;
        }

        LoggerHelper.Info($"[RestartGameAction] 通过 CLI 重启模拟器实例 {_mumuIndex}...");
        var restartPsi = new ProcessStartInfo(_mumuCliExe, $"control --vmindex {_mumuIndex} restart")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        bool cliOk = false;
        try
        {
            using var proc = Process.Start(restartPsi);
            if (proc != null)
            {
                if (!proc.WaitForExit(30000))
                {
                    // 超时未退出，不能直接读 ExitCode（会抛异常），按失败处理
                    LoggerHelper.Info("[RestartGameAction] CLI 重启命令超时，按失败处理");
                    try { proc.Kill(); } catch { }
                }
                else if (proc.ExitCode != 0)
                {
                    var output = proc.StandardOutput.ReadToEnd();
                    var error = proc.StandardError.ReadToEnd();
                    LoggerHelper.Info($"[RestartGameAction] CLI 重启返回异常: code={proc.ExitCode} out={output.Trim()} err={error.Trim()}");
                }
                else
                {
                    cliOk = true;
                }
            }
        }
        catch (Exception e)
        {
            LoggerHelper.Info($"[RestartGameAction] CLI 重启异常: {e.Message}");
        }

        // CLI 失败时回退：先试旧版方式，不可用则强制重启（覆盖模拟器无响应场景）
        if (!cliOk)
        {
            LoggerHelper.Info("[RestartGameAction] CLI 重启未成功，回退到旧版方式");
            if (RestartEmulatorLegacy())
                return true;
            LoggerHelper.Info("[RestartGameAction] 旧版方式不可用，尝试强制重启模拟器进程");
            return RestartEmulatorForce();
        }

        // 等待 ADB 重新连接
        LoggerHelper.Info("[RestartGameAction] 等待模拟器启动...");
        WaitForRecovery(10000);
        if (!WaitForAdbReady(30))
        {
            LoggerHelper.Info("[RestartGameAction] 模拟器启动超时，尝试强制重启模拟器进程");
            return RestartEmulatorForce();
        }

        return true;
    }

    /// <summary>
    /// 无响应场景强制重启：taskkill 强杀 MuMuNxDevice.exe（挂起/无响应进程也能强杀），
    /// 再用 mumu-cli 重新启动实例。覆盖 CLI 重启超时且旧版 MuMuPlayer.exe 不存在的情况。
    /// </summary>
    private bool RestartEmulatorForce()
    {
        _recoveryToken.ThrowIfCancellationRequested();
        // 1. 强杀设备进程（无响应进程强杀不需要进程响应）
        LoggerHelper.Info("[RestartGameAction] 强制结束 MuMuNxDevice.exe...");
        var killPsi = new ProcessStartInfo("taskkill", "/F /IM MuMuNxDevice.exe /T")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        };
        try
        {
            using var killProc = Process.Start(killPsi);
            killProc?.WaitForExit(5000);
        }
        catch (Exception e)
        {
            LoggerHelper.Info($"[RestartGameAction] 强杀模拟器进程异常: {e.Message}");
        }
        WaitForRecovery(5000);

        // 2. 用 mumu-cli 重新启动实例
        if (!string.IsNullOrWhiteSpace(_mumuCliExe))
        {
            LoggerHelper.Info($"[RestartGameAction] 通过 CLI 重新启动模拟器实例 {_mumuIndex}...");
            var launchPsi = new ProcessStartInfo(_mumuCliExe, $"control --vmindex {_mumuIndex} launch")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            try
            {
                using var launchProc = Process.Start(launchPsi);
                if (launchProc != null && launchProc.WaitForExit(15000) && launchProc.ExitCode == 0)
                {
                    LoggerHelper.Info("[RestartGameAction] 模拟器实例启动命令已提交");
                }
                else
                {
                    LoggerHelper.Info("[RestartGameAction] 模拟器实例启动命令超时或失败，继续等待");
                }
            }
            catch (Exception e)
            {
                LoggerHelper.Info($"[RestartGameAction] 启动模拟器异常: {e.Message}");
            }
        }

        // 3. 等待 ADB 重新连接
        LoggerHelper.Info("[RestartGameAction] 等待模拟器启动...");
        WaitForRecovery(10000);
        var ready = WaitForAdbReady(30);
        if (!ready)
            LoggerHelper.Info("[RestartGameAction] 模拟器启动超时，继续后续流程");
        return ready;
    }

    /// <summary>
    /// 旧版 MuMu：杀 MuMuPlayer.exe 进程后重新启动
    /// </summary>
    /// <returns>是否成功执行了模拟器重启（无可用主程序时返回 false）</returns>
    private bool RestartEmulatorLegacy()
    {
        _recoveryToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(_mumuLegacyExe) || string.IsNullOrWhiteSpace(_mumuProcessName))
        {
            LoggerHelper.Info("[RestartGameAction] 未找到 MuMu 主程序，跳过模拟器重启");
            return false;
        }

        LoggerHelper.Info($"[RestartGameAction] 正在关闭模拟器（{_mumuProcessName}）...");
        var killPsi = new ProcessStartInfo("taskkill", $"/F /IM {_mumuProcessName}.exe /T")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        };
        try
        {
            using var killProc = Process.Start(killPsi);
            killProc?.WaitForExit(5000);
        }
        catch (Exception e)
        {
            LoggerHelper.Info($"[RestartGameAction] 关闭模拟器异常: {e.Message}");
        }
        WaitForRecovery(3000);

        LoggerHelper.Info($"[RestartGameAction] 正在启动模拟器（{_mumuLegacyExe}）...");
        var startArgs = _mumuIndex > 0 ? $"-v {_mumuIndex}" : "";
        var startPsi = new ProcessStartInfo(_mumuLegacyExe, startArgs)
        {
            UseShellExecute = true,
        };
        try
        {
            Process.Start(startPsi);
        }
        catch (Exception e)
        {
            LoggerHelper.Info($"[RestartGameAction] 启动模拟器异常: {e.Message}");
            return false;
        }

        LoggerHelper.Info("[RestartGameAction] 等待模拟器启动...");
        WaitForRecovery(15000);
        if (!WaitForAdbReady(30))
            LoggerHelper.Info("[RestartGameAction] 模拟器启动超时，继续后续流程");
        return true;
    }

    /// <summary>
    /// 等待 ADB 重新连接，最多尝试 maxAttempts 次
    /// </summary>
    private bool WaitForAdbReady(int maxAttempts, int timeoutMs = 5000)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            _recoveryToken.ThrowIfCancellationRequested();
            try
            {
                var checkPsi = new ProcessStartInfo(_adbPath!, $"shell echo ready")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                };
                if (!string.IsNullOrWhiteSpace(_adbSerial))
                    checkPsi.Arguments = $"-s {_adbSerial} shell echo ready";

                using var checkProc = Process.Start(checkPsi);
                if (checkProc == null)
                {
                    WaitForRecovery(2000);
                    continue;
                }
                checkProc.WaitForExit(timeoutMs);
                // WaitForExit 超时后进程可能仍在运行，直接读 ExitCode 会抛异常，先判断是否已退出
                if (checkProc.HasExited && checkProc.ExitCode == 0)
                {
                    LoggerHelper.Info("[RestartGameAction] 模拟器已就绪");
                    return true;
                }
            }
            catch (Exception e)
            {
                LoggerHelper.Info($"[RestartGameAction] ADB 检查异常: {e.Message}");
            }
            WaitForRecovery(2000);
        }
        return false;
    }

    /// <summary>
    /// 执行一条 adb 命令，并返回命令是否成功
    /// </summary>
    private bool RunAdbCommand(string adbPath, string adbSerial, string args, out string output)
    {
        _recoveryToken.ThrowIfCancellationRequested();
        output = "";
        var psi = new ProcessStartInfo(adbPath, args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (!string.IsNullOrWhiteSpace(adbSerial))
            psi.Arguments = $"-s {adbSerial} {args}";
        try
        {
            using var proc = Process.Start(psi);
            if (proc == null)
            {
                LoggerHelper.Error("[RestartGameAction] 无法启动 ADB 进程");
                return false;
            }

            var outputTask = proc.StandardOutput.ReadToEndAsync();
            var errorTask = proc.StandardError.ReadToEndAsync();
            if (!proc.WaitForExit(10000))
            {
                LoggerHelper.Error($"[RestartGameAction] ADB 命令执行超时: {args}");
                try { proc.Kill(true); } catch { }
                return false;
            }

            output = outputTask.GetAwaiter().GetResult().Trim();
            var error = errorTask.GetAwaiter().GetResult().Trim();
            if (proc.ExitCode != 0)
            {
                LoggerHelper.Error($"[RestartGameAction] ADB 命令执行失败: code={proc.ExitCode} out={output} err={error}");
                return false;
            }

            if (!string.IsNullOrWhiteSpace(error))
                LoggerHelper.Warning($"[RestartGameAction] ADB 命令返回警告: {error}");
            return true;
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"[RestartGameAction] ADB 命令执行异常: {e.Message}");
            return false;
        }
    }

    private bool RunAdbCommand(string adbPath, string adbSerial, string args)
    {
        return RunAdbCommand(adbPath, adbSerial, args, out _);
    }

    private bool TryResolveLaunchActivity(string package, out string launchActivity)
    {
        launchActivity = "";
        if (!RunAdbCommand(
                _adbPath!,
                _adbSerial ?? "",
                $"shell cmd package resolve-activity --brief -a android.intent.action.MAIN -c android.intent.category.LAUNCHER {package}",
                out var output))
            return false;

        var component = output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .LastOrDefault(line => line.StartsWith($"{package}/", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(component))
            return false;

        launchActivity = component;
        LoggerHelper.Info($"[RestartGameAction] 已解析游戏启动 Activity: {launchActivity}");
        return true;
    }

    private bool TryRestartGame(string package)
    {
        _recoveryToken.ThrowIfCancellationRequested();
        LoggerHelper.Info($"[RestartGameAction] 强制停止游戏进程: {package}");
        if (!RunAdbCommand(_adbPath!, _adbSerial ?? "", $"shell am force-stop {package}"))
            LoggerHelper.Warning("[RestartGameAction] 强制停止游戏失败，继续尝试启动游戏");
        WaitForRecovery(2000);

        LoggerHelper.Info($"[RestartGameAction] 重新启动游戏: {package}");
        if (TryResolveLaunchActivity(package, out var launchActivity))
        {
            if (!RunAdbCommand(_adbPath!, _adbSerial ?? "", $"shell am start -n {launchActivity}"))
            {
                LoggerHelper.Warning("[RestartGameAction] 使用已解析的 Activity 启动游戏失败");
                return false;
            }
        }
        else if (!RunAdbCommand(
                     _adbPath!,
                     _adbSerial ?? "",
                     $"shell am start -a android.intent.action.MAIN -c android.intent.category.LAUNCHER -p {package}"))
        {
            LoggerHelper.Warning("[RestartGameAction] 游戏启动失败");
            return false;
        }

        LoggerHelper.Info("[RestartGameAction] 游戏重启完成");
        return true;
    }

    /// <summary>
    /// 从当前处理器收集模拟器环境，优先重启游戏；仅在游戏重启失败时重启模拟器后重试。
    /// 供 pipeline node 与 MATR 层卡死循环检测恢复复用。
    /// </summary>
    public static void RestartAndReloadGame(bool logAutoRecovery = true, MaaProcessor? processor = null,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        processor ??= MaaProcessorManager.Instance.Current;
        if (!token.CanBeCanceled && processor?.CancellationTokenSource != null)
            token = processor.CancellationTokenSource.Token;
        token.ThrowIfCancellationRequested();
        if (processor != null) processor.IsGameRecoveryRunning = true;
        try
        {
            if (logAutoRecovery)
                processor?.LogAutoRecovery("任务流程触发重启");
            var action = new RestartGameAction { _recoveryToken = token };
            action.EnsureAdbInfo(processor);

            var package = GetPackageName();

            if (action.TryRestartGame(package))
                return;

            LoggerHelper.Warning("[RestartGameAction] 游戏重启失败，开始重启模拟器");
            if (!action.RestartEmulator())
            {
                LoggerHelper.Error("[RestartGameAction] 模拟器重启失败，无法继续恢复游戏");
                throw new InvalidOperationException("模拟器重启失败，请检查模拟器路径、实例状态和 ADB 连接");
            }

            if (!action.TryRestartGame(package))
            {
                LoggerHelper.Warning("[RestartGameAction] 模拟器重启成功，但游戏启动失败，继续交由任务流程进入主枢纽");
                return;
            }
        }
        finally
        {
            if (processor != null) processor.IsGameRecoveryRunning = false;
        }
    }

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            ActionParamHelper.ThrowIfStopping(context);
            var parameters = ActionParamHelper.Parse(args.ActionParam);
            var logAutoRecovery = (bool?)parameters["log_auto_recovery"] ?? true;
            RestartAndReloadGame(logAutoRecovery);
            return true;
        }
        catch (MaaStopException)
        {
            LoggerHelper.Info("[RestartGameAction] 检测到手动停止，已取消执行");
            return false;
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"[RestartGameAction] 错误: {e.Message}");
            return false;
        }
    }
}
