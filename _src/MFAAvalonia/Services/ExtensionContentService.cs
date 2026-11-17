using System;
using System.IO;
using System.Linq;
using MFAAvalonia.Helper;

namespace MFAAvalonia.Services;

/// <summary>只检测安装根目录 extension 目录中的扩展包，不执行外部代码。</summary>
public static class ExtensionContentService
{
    private static readonly Lazy<bool> ContentAvailable = new(DetectContent);

    /// <summary>本次启动共用的检测结果，安装或移除后重启生效。</summary>
    public static bool HasContent() => ContentAvailable.Value;

    private static bool DetectContent()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            var directory = Path.Combine(AppPaths.InstallRoot, "extension");
            return Directory.Exists(directory)
                && Directory.EnumerateFiles(directory)
                    .Any(path => string.Equals(Path.GetExtension(path), ".matrext", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LoggerHelper.Warning($"检查扩展文件失败：{exception.Message}");
            return false;
        }
    }
}
