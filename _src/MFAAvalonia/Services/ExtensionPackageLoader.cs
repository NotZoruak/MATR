using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using MATR.ExtensionSdk;

namespace MFAAvalonia.Services;

/// <summary>扩展清单的接口版本与工作区入口，扫描阶段不执行程序集。</summary>
public sealed record ExtensionManifest(string Id, string Name, string Version, int ApiVersion,
    string EntryAssembly, string EntryType, int Order = 100, string Icon = "Apps");
public sealed record ExtensionPackage(string Path, string Hash, ExtensionManifest Manifest);
public sealed record ExtensionPackageCatalog(IReadOnlyList<ExtensionPackage> Packages, IReadOnlyList<string> Errors);

/// <summary>校验独立扩展包，拒绝不兼容或路径越界的内容。</summary>
public static class ExtensionPackageLoader
{
    private const long MaxPackageBytes = 32 * 1024 * 1024;
    private const long MaxExpandedBytes = 64 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static ExtensionPackageCatalog Scan(string directory)
    {
        var packages = new List<ExtensionPackage>();
        var errors = new List<string>();
        if (!Directory.Exists(directory)) return new(packages, errors);
        try
        {
            var files = Directory.EnumerateFiles(directory).Where(x => string.Equals(System.IO.Path.GetExtension(x), ".matrext", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToArray();
            if (files.Length > 128) errors.Add("扩展数量超过128，只扫描前128个包。");
            foreach (var file in files.Take(128))
                try
                {
                    using var stream = File.OpenRead(file);
                    if (stream.Length > MaxPackageBytes) throw new InvalidDataException("压缩包超过32MB。");
                    var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
                    stream.Position = 0;
                    using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                    var manifest = Validate(archive);
                    packages.Add(new(file, hash, manifest));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException or BadImageFormatException or InvalidDataException)
                { errors.Add(System.IO.Path.GetFileName(file) + "：" + exception.Message); }
            var duplicates = packages.GroupBy(x => x.Manifest.Id, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1).Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var id in duplicates) errors.Add("扩展标识重复，已跳过全部同名包：" + id);
            return new(packages.Where(x => !duplicates.Contains(x.Manifest.Id)).OrderBy(x => x.Manifest.Order).ThenBy(x => x.Manifest.Id, StringComparer.Ordinal).ToArray(), errors);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { errors.Add("读取扩展目录失败：" + exception.Message); return new(packages, errors); }
    }

    private static ExtensionManifest Validate(ZipArchive archive)
    {
        if (archive.Entries.Count > 256) throw new InvalidDataException("包内文件数量超过256。");
        long size = 0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            ValidatePath(entry.FullName);
            if (!names.Add(entry.FullName)) throw new InvalidDataException("包内存在重复路径。");
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("包内不允许符号链接。");
            size = checked(size + entry.Length);
            if (size > MaxExpandedBytes) throw new InvalidDataException("解压内容超过64MB。");
        }
        var manifestEntry = archive.GetEntry("manifest.json") ?? throw new InvalidDataException("缺少 manifest.json。");
        if (manifestEntry.Length > 65536) throw new InvalidDataException("清单过大。");
        using var reader = manifestEntry.Open();
        var manifest = JsonSerializer.Deserialize<ExtensionManifest>(reader, JsonOptions) ?? throw new InvalidDataException("清单为空。");
        if (manifest.ApiVersion != 1) throw new InvalidDataException("不支持的扩展接口版本。");
        if (string.IsNullOrWhiteSpace(manifest.Id) || !Regex.IsMatch(manifest.Id, @"^[a-zA-Z0-9][a-zA-Z0-9._-]{0,79}$")) throw new InvalidDataException("扩展标识无效。");
        if (string.IsNullOrWhiteSpace(manifest.Name) || manifest.Name.Length > 80 || string.IsNullOrWhiteSpace(manifest.Version) || string.IsNullOrWhiteSpace(manifest.EntryType)) throw new InvalidDataException("清单缺少名称、版本或入口类型。");
        ValidatePath(manifest.EntryAssembly);
        if (!manifest.EntryAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || archive.GetEntry(manifest.EntryAssembly) == null) throw new InvalidDataException("入口程序集不存在。");
        using var assemblyBytes = new MemoryStream();
        using (var source = archive.GetEntry(manifest.EntryAssembly)!.Open()) source.CopyTo(assemblyBytes);
        assemblyBytes.Position = 0;
        using var pe = new PEReader(assemblyBytes);
        if (!pe.HasMetadata) throw new InvalidDataException("入口不是托管程序集。");
        var metadata = pe.GetMetadataReader();
        var entryTypes = metadata.TypeDefinitions.Select(metadata.GetTypeDefinition).Where(type =>
            metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name) == manifest.EntryType).ToArray();
        if (!metadata.IsAssembly || entryTypes.Length != 1) throw new InvalidDataException("程序集不存在指定入口类型。");
        var entryType = entryTypes[0];
        bool ImplementsWorkspace(TypeDefinition type, int depth = 0)
        {
            if (depth > 64) throw new InvalidDataException("入口类型继承关系过深。");
            foreach (var handle in type.GetInterfaceImplementations())
            {
                var implementation = metadata.GetInterfaceImplementation(handle).Interface;
                if (implementation.Kind != HandleKind.TypeReference) continue;
                var reference = metadata.GetTypeReference((TypeReferenceHandle)implementation);
                if (metadata.GetString(reference.Namespace) == "MATR.ExtensionSdk" && metadata.GetString(reference.Name) == "IExtensionWorkspace") return true;
            }
            return type.BaseType.Kind == HandleKind.TypeDefinition && ImplementsWorkspace(metadata.GetTypeDefinition((TypeDefinitionHandle)type.BaseType), depth + 1);
        }
        var constructible = entryType.GetMethods().Select(metadata.GetMethodDefinition).Any(method =>
        {
            if (metadata.GetString(method.Name) != ".ctor" || (method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public) return false;
            var signature = metadata.GetBlobReader(method.Signature);
            var header = signature.ReadSignatureHeader();
            if (header.IsGeneric) signature.ReadCompressedInteger();
            return signature.ReadCompressedInteger() == 0;
        });
        if ((entryType.Attributes & TypeAttributes.VisibilityMask) != TypeAttributes.Public || (entryType.Attributes & TypeAttributes.Abstract) != 0
            || !constructible || !ImplementsWorkspace(entryType)) throw new InvalidDataException("入口类型必须公开、实现扩展接口并提供无参构造方法。");
        return manifest;
    }

    private static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.Contains('\\') || path.Contains(':') || path.Contains('\0')
            || path.Split('/').Any(x => x is "." or "..") || path.TrimEnd('/').Split('/').Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("包内路径无效或越界。");
    }

    public static LoadedExtension Load(ExtensionPackage package, string cacheRoot)
    {
        // 扫描后文件可能被替换，加载时重新校验同一份内容。
        using var stream = File.OpenRead(package.Path);
        if (stream.Length > MaxPackageBytes || Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant() != package.Hash)
            throw new InvalidDataException("扩展包已经改变，请重启 MATR。");
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        Validate(archive);
        var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(cacheRoot, package.Manifest.Id, package.Hash, Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        foreach (var entry in archive.Entries)
        {
            var target = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, entry.FullName.Replace('/', System.IO.Path.DirectorySeparatorChar)));
            if (!target.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new InvalidDataException("解包路径越界。");
            if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            using var input = entry.Open();
            using var output = File.Create(target);
            input.CopyTo(output);
        }
        var context = new PackageLoadContext(root, System.IO.Path.Combine(root, package.Manifest.EntryAssembly));
        try
        {
            var assembly = context.LoadFromAssemblyPath(System.IO.Path.Combine(root, package.Manifest.EntryAssembly));
            var type = assembly.GetType(package.Manifest.EntryType, true)!;
            if (!typeof(IExtensionWorkspace).IsAssignableFrom(type) || type.IsAbstract || type.GetConstructor(Type.EmptyTypes) == null)
                throw new InvalidDataException("入口类型没有实现扩展接口或缺少无参构造方法。");
            return new LoadedExtension((IExtensionWorkspace)Activator.CreateInstance(type)!, root, context);
        }
        catch { context.Unload(); throw; }
    }

    private sealed class PackageLoadContext(string directory, string entryPath) : AssemblyLoadContext(isCollectible: true)
    {
        private readonly AssemblyDependencyResolver _resolver = new(entryPath);
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var name = assemblyName.Name ?? "";
            var shared = name is "MATR.ExtensionSdk" or "MFAAvalonia.Core" or "SukiUI" or "netstandard" or "mscorlib"
                || name.StartsWith("Avalonia", StringComparison.Ordinal) || name.StartsWith("FluentIcons", StringComparison.Ordinal)
                || File.Exists(System.IO.Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), name + ".dll"));
            if (!shared)
            {
                var resolved = _resolver.ResolveAssemblyToPath(assemblyName);
                var candidates = new[] { resolved, System.IO.Path.Combine(System.IO.Path.GetDirectoryName(entryPath)!, name + ".dll"), System.IO.Path.Combine(directory, name + ".dll") };
                foreach (var candidate in candidates.Where(x => x != null))
                {
                    var path = System.IO.Path.GetFullPath(candidate!);
                    if (path.StartsWith(directory + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal) && File.Exists(path))
                        return LoadFromAssemblyPath(path);
                }
            }
            // 公共 UI 类型共享；缺少私有副本时才回退到宿主已有的依赖。
            return Default.Assemblies.FirstOrDefault(x => AssemblyName.ReferenceMatchesDefinition(x.GetName(), assemblyName));
        }
    }

}

public sealed class LoadedExtension(IExtensionWorkspace workspace, string directory, AssemblyLoadContext context) : IDisposable
{
    public IExtensionWorkspace Workspace { get; } = workspace;
    public string Directory { get; } = directory;
    public void Dispose()
    {
        try { Workspace.Dispose(); }
        finally { context.Unload(); }
    }
}
