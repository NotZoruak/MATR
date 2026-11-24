using System.IO;
namespace MFAAvalonia.Services;

/// <summary>读取随应用分发的静态游戏资料。</summary>
internal static class CatalogResources
{
    internal static string Read(string name)
    {
        using var stream = typeof(CatalogResources).Assembly.GetManifestResourceStream("MFAAvalonia.Assets." + name)
            ?? throw new FileNotFoundException("游戏资料缺失：" + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
