using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MFAAvalonia.Services;

/// <summary>计划任务单次操作的结果。</summary>
public sealed record WindowsScheduledTaskOperationResult(bool Succeeded, string? ErrorMessage)
{
    /// <summary>操作成功。</summary>
    public static WindowsScheduledTaskOperationResult Success { get; } = new(true, null);

    /// <summary>操作失败，并附带系统返回的错误信息。</summary>
    public static WindowsScheduledTaskOperationResult Failure(string message) => new(false, message);
}

/// <summary>
/// 系统任务计划程序的可注入边界。同步逻辑只依赖本接口，
/// 自动化测试可以注入空实现，从而不在测试中创建真实系统任务。
/// </summary>
public interface IWindowsScheduledTaskClient
{
    /// <summary>列出 MATR 任务文件夹中现有的任务名称，读取失败时返回空集合。</summary>
    IReadOnlyList<string> ListManagedTaskNames();

    /// <summary>读取指定任务的 XML，任务不存在或读取失败时返回 null。</summary>
    string? TryReadTaskXml(string taskName);

    /// <summary>创建或覆盖指定任务。</summary>
    WindowsScheduledTaskOperationResult CreateOrUpdate(string taskName, string xml);

    /// <summary>删除指定任务。</summary>
    WindowsScheduledTaskOperationResult Delete(string taskName);
}

/// <summary>通过 schtasks.exe 管理系统计划任务的客户端，不需要管理员权限。</summary>
public sealed class SchTasksScheduledTaskClient : IWindowsScheduledTaskClient
{
    private const string SchTasksFileName = "schtasks.exe";
    private const int CommandTimeoutMilliseconds = 20000;

    private static readonly Lazy<Encoding> ConsoleEncoding = new(ResolveConsoleEncoding);

    private readonly string _tempDirectory;

    public SchTasksScheduledTaskClient(string? tempDirectory = null)
    {
        _tempDirectory = string.IsNullOrWhiteSpace(tempDirectory)
            ? Path.Combine(Path.GetTempPath(), "MATR", "scheduled-tasks")
            : tempDirectory;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ListManagedTaskNames()
    {
        var result = RunSchTasks(
            ["/Query", "/TN", $"{WindowsScheduledTaskDefinitionBuilder.TaskFolderName}\\", "/FO", "CSV", "/NH"],
            CommandTimeoutMilliseconds);

        return result.ExitCode == 0 ? ParseManagedTaskNames(result.Output) : [];
    }

    /// <inheritdoc />
    public string? TryReadTaskXml(string taskName)
    {
        var result = RunSchTasks(BuildQueryArguments(taskName), CommandTimeoutMilliseconds);
        return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Output) ? result.Output : null;
    }

    /// <inheritdoc />
    public WindowsScheduledTaskOperationResult CreateOrUpdate(string taskName, string xml)
    {
        var xmlPath = Path.Combine(_tempDirectory, $"matr-timer-{Guid.NewGuid():N}.xml");
        try
        {
            Directory.CreateDirectory(_tempDirectory);
            // schtasks.exe 要求任务 XML 为 Unicode 编码。
            File.WriteAllText(xmlPath, xml, Encoding.Unicode);
            var result = RunSchTasks(BuildCreateArguments(taskName, xmlPath), CommandTimeoutMilliseconds);
            return result.ExitCode == 0
                ? WindowsScheduledTaskOperationResult.Success
                : WindowsScheduledTaskOperationResult.Failure(Describe(result));
        }
        catch (Exception exception)
        {
            return WindowsScheduledTaskOperationResult.Failure(exception.Message);
        }
        finally
        {
            try
            {
                if (File.Exists(xmlPath))
                    File.Delete(xmlPath);
            }
            catch (Exception)
            {
                // 临时文件清理失败不影响同步结果。
            }
        }
    }

    /// <inheritdoc />
    public WindowsScheduledTaskOperationResult Delete(string taskName)
    {
        var result = RunSchTasks(BuildDeleteArguments(taskName), CommandTimeoutMilliseconds);
        return result.ExitCode == 0
            ? WindowsScheduledTaskOperationResult.Success
            : WindowsScheduledTaskOperationResult.Failure(Describe(result));
    }

    /// <summary>生成查询任务 XML 的参数。</summary>
    public static IReadOnlyList<string> BuildQueryArguments(string taskName)
    {
        return ["/Query", "/TN", WindowsScheduledTaskDefinitionBuilder.BuildTaskPath(taskName), "/XML"];
    }

    /// <summary>生成创建或覆盖任务的参数。</summary>
    public static IReadOnlyList<string> BuildCreateArguments(string taskName, string xmlPath)
    {
        return
        [
            "/Create",
            "/TN", WindowsScheduledTaskDefinitionBuilder.BuildTaskPath(taskName),
            "/XML", xmlPath,
            "/F"
        ];
    }

    /// <summary>生成删除任务的参数。</summary>
    public static IReadOnlyList<string> BuildDeleteArguments(string taskName)
    {
        return
        [
            "/Delete",
            "/TN", WindowsScheduledTaskDefinitionBuilder.BuildTaskPath(taskName),
            "/F"
        ];
    }

    /// <summary>解析 CSV 任务列表输出中的任务名称。</summary>
    public static IReadOnlyList<string> ParseManagedTaskNames(string? csvOutput)
    {
        if (string.IsNullOrWhiteSpace(csvOutput))
            return [];

        var names = new List<string>();
        foreach (var line in csvOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim().TrimEnd('\r');
            if (trimmed.Length == 0 || trimmed[0] != '"')
                continue;

            var end = trimmed.IndexOf('"', 1);
            if (end <= 1)
                continue;

            var path = trimmed[1..end];
            var separatorIndex = path.LastIndexOf('\\');
            var name = separatorIndex >= 0 ? path[(separatorIndex + 1)..] : path;
            if (name.StartsWith(WindowsScheduledTaskDefinitionBuilder.TaskNamePrefix, StringComparison.OrdinalIgnoreCase))
                names.Add(name);
        }

        return names
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string Describe((int ExitCode, string Output) result)
    {
        var message = string.IsNullOrWhiteSpace(result.Output)
            ? $"schtasks.exe 返回退出码 {result.ExitCode}"
            : result.Output.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return message.Length > 300 ? message[..300] : message;
    }

    private static (int ExitCode, string Output) RunSchTasks(IReadOnlyList<string> arguments, int timeoutMilliseconds)
    {
        try
        {
            var startInfo = new ProcessStartInfo(SchTasksFileName)
            {
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                StandardOutputEncoding = ConsoleEncoding.Value,
                StandardErrorEncoding = ConsoleEncoding.Value
            };

            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo);
            if (process == null)
                return (-1, "无法启动 schtasks.exe");

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception)
                {
                    // 进程已经被回收时忽略。
                }

                return (-1, "执行 schtasks.exe 超时");
            }

            var output = string.Join(
                " ",
                new[] { SafeResult(outputTask), SafeResult(errorTask) }
                    .Where(text => !string.IsNullOrWhiteSpace(text))).Trim();
            return (process.ExitCode, output);
        }
        catch (Exception exception)
        {
            return (-1, exception.Message);
        }
    }

    private static string SafeResult(Task<string> task)
    {
        try
        {
            return task.GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static Encoding ResolveConsoleEncoding()
    {
        try
        {
            // 系统自带工具按当前代码页输出文本，按代码页解码可以避免中文提示变成乱码。
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch (Exception)
        {
            return Encoding.UTF8;
        }
    }
}
