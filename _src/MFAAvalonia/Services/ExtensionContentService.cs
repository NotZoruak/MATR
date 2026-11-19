using System;
using System.IO;
using MFAAvalonia.Helper;

namespace MFAAvalonia.Services;

/// <summary>启动时扫描有效扩展清单，不在入口检测阶段执行包内代码。</summary>
public static class ExtensionContentService
{
    private static readonly Lazy<ExtensionPackageCatalog> Catalog = new(() =>
    {
        var catalog = ExtensionPackageLoader.Scan(Path.Combine(AppPaths.InstallRoot, "extension"));
        foreach (var error in catalog.Errors) LoggerHelper.Warning("扩展包校验失败：" + error);
        return catalog;
    });
    public static ExtensionPackageCatalog Content => Catalog.Value;
    public static bool HasContent() => Content.Packages.Count > 0;
}
