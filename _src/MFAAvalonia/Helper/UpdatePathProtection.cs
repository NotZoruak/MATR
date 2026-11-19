using System;
using System.IO;

namespace MFAAvalonia.Helper;

/// <summary>
/// 更新只能管理安装包内容，安装根目录的扩展由用户独立管理。
/// </summary>
public static class UpdatePathProtection
{
    private static string Normalize(string path) => Path.GetFullPath(
        path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar))
        .TrimEnd(Path.DirectorySeparatorChar);

    public static bool IsProtected(string installRoot, string targetPath)
    {
        var extensionRoot = Normalize(Path.Combine(installRoot, "extension"));
        var target = Normalize(targetPath);
        return target.Equals(extensionRoot, StringComparison.OrdinalIgnoreCase)
            || target.StartsWith(extensionRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static bool ContainsProtectedDirectory(string installRoot, string directoryPath)
    {
        var directory = Normalize(directoryPath);
        var extensionRoot = Normalize(Path.Combine(installRoot, "extension"));
        return IsProtected(installRoot, directoryPath)
            || extensionRoot.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsProtectedRelativePath(string relativePath) =>
        IsProtected(Path.GetTempPath(), Path.Combine(Path.GetTempPath(),
            relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)));
}
