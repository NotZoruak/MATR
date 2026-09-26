using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MFAAvalonia.Helper;

/// <summary>
/// 日志导出时的文件选择规则：命名 Maa 日志（含轮转备份）、GUI 日志、自定义日志与图片分类。
/// 抽成独立类型便于单测，避免再次出现「勾选 GUI 日志却导不出来」「轮转日志进不了包」这类问题。
/// </summary>
public static class LogExportSelection
{
    /// <summary>导出时排除的目录：视觉调试图体量大且与故障排查无关。</summary>
    public const string VisionFolder = "vision";

    private static readonly string[] ImageExtensions =
    [
        ".jpg",
        ".jpeg",
        ".png",
        ".bmp",
        ".gif",
        ".webp",
    ];

    /// <summary>
    /// 命名 Maa 日志：`maafw.log`、`maa.log`，以及 `maafw.bak.*.log`、`maa.bak.*.log` 等轮转文件。
    /// 轮转文件往往正是崩溃或卡死现场的那一份，必须一并导出。
    /// </summary>
    public static bool IsMaaLogFileName(string? fileName)
        => !string.IsNullOrWhiteSpace(fileName)
           && fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
           && fileName.StartsWith("maa", StringComparison.OrdinalIgnoreCase);

    /// <summary>自定义日志文件名（`custom.log*`），只随「自定义日志」勾选项导出。</summary>
    public static bool IsCustomLogFileName(string? fileName)
        => !string.IsNullOrWhiteSpace(fileName)
           && fileName.StartsWith("custom.log", StringComparison.OrdinalIgnoreCase);

    /// <summary>收集 debug 目录下的 Maa 日志；没有命名 Maa 日志时退回「所有 .log」以兼容自定义命名。</summary>
    public static IReadOnlyList<string> SelectMaaLogFiles(string? debugDirectory)
    {
        if (string.IsNullOrWhiteSpace(debugDirectory) || !Directory.Exists(debugDirectory))
            return [];

        var allFiles = Directory.GetFiles(debugDirectory, "*", SearchOption.AllDirectories);
        var namedLogs = allFiles
            .Where(file => IsMaaLogFileName(Path.GetFileName(file)))
            .ToList();
        if (namedLogs.Count > 0)
            return namedLogs;

        return allFiles
            .Where(file => Path.GetExtension(file).Equals(".log", StringComparison.OrdinalIgnoreCase)
                           && !IsCustomLogFileName(Path.GetFileName(file))
                           && !IsPathUnderFolder(file, VisionFolder))
            .ToList();
    }

    /// <summary>收集数据根目录下的自定义日志。</summary>
    public static IReadOnlyList<string> SelectCustomLogFiles(string? dataRoot)
    {
        if (string.IsNullOrWhiteSpace(dataRoot) || !Directory.Exists(dataRoot))
            return [];

        return Directory.GetFiles(dataRoot, "*", SearchOption.AllDirectories)
            .Where(file => IsCustomLogFileName(Path.GetFileName(file)))
            .ToList();
    }

    /// <summary>
    /// 收集给定目录下的全部 .log。GUI 日志分布在安装目录的 debug/logs 与历史根目录 logs 两处，
    /// 调用方两处都要传，避免再出现勾选了 GUI 日志却导不出来的情况。
    /// </summary>
    public static IReadOnlyList<string> SelectLogFilesInDirectories(params string?[] directories)
    {
        var results = new List<string>();
        foreach (var directory in directories)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                continue;
            results.AddRange(Directory.GetFiles(directory, "*.log", SearchOption.AllDirectories));
        }

        return results.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static bool IsImageFile(string? file)
        => !string.IsNullOrWhiteSpace(file)
           && ImageExtensions.Contains(Path.GetExtension(file).ToLowerInvariant());

    public static bool IsPathUnderFolder(string? file, string folderName)
    {
        if (string.IsNullOrWhiteSpace(file) || string.IsNullOrWhiteSpace(folderName))
            return false;

        var directory = Path.GetDirectoryName(file);
        if (string.IsNullOrWhiteSpace(directory))
            return false;

        return directory
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment.Equals(folderName, StringComparison.OrdinalIgnoreCase));
    }
}
