using System;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using MFAAvalonia.Helper;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public sealed class UniversalExtractorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MATR解压测试", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(".zip", false)]
    [InlineData(".zip", true)]
    [InlineData(".tar", false)]
    [InlineData(".tar", true)]
    [InlineData(".tgz", false)]
    [InlineData(".tgz", true)]
    [InlineData(".tar.gz", false)]
    [InlineData(".tar.gz", true)]
    [InlineData(".bin", false)]
    [InlineData(".bin", true)]
    public async Task 升级后应保持常用压缩格式和流式备选解压可用(string extension, bool asynchronous)
    {
        var archivePath = CreateArchive(extension, "目录/内容.txt");
        var destination = Path.Combine(_root, "输出");

        var result = asynchronous
            ? await UniversalExtractor.ExtractAsync(archivePath, destination)
            : UniversalExtractor.Extract(archivePath, destination);

        Assert.True(result);
        Assert.Equal("解压验证", File.ReadAllText(Path.Combine(destination, "目录", "内容.txt")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 恶意压缩包不得写入目标目录以外(bool asynchronous)
    {
        var archivePath = CreateArchive(".zip", "../越界.txt");
        var destination = Path.Combine(_root, "输出");

        var result = asynchronous
            ? await UniversalExtractor.ExtractAsync(archivePath, destination)
            : UniversalExtractor.Extract(archivePath, destination);

        Assert.False(result);
        Assert.False(File.Exists(Path.Combine(_root, "越界.txt")));
    }

    private string CreateArchive(string extension, string entryName)
    {
        Directory.CreateDirectory(_root);
        var archivePath = Path.Combine(_root, "测试包" + extension);
        using var output = File.Create(archivePath);
        if (extension is ".zip" or ".bin")
        {
            using var archive = new ZipArchive(output, ZipArchiveMode.Create);
            using var writer = new StreamWriter(archive.CreateEntry(entryName).Open(), Encoding.UTF8);
            writer.Write("解压验证");
        }
        else
        {
            using var gzip = extension is ".tgz" or ".tar.gz" ? new GZipStream(output, CompressionMode.Compress, leaveOpen: true) : null;
            using var archive = new TarWriter((Stream?)gzip ?? output, leaveOpen: true);
            using var data = new MemoryStream(Encoding.UTF8.GetBytes("解压验证"));
            archive.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, entryName) { DataStream = data });
        }
        return archivePath;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
