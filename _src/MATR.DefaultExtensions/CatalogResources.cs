using System.IO;
namespace MATR.DefaultExtensions;

/// <summary>读取随默认扩展分发的静态刀剑资料。</summary>
internal static class CatalogResources
{
    internal static string Read(string name)
    {
        using var stream = typeof(CatalogResources).Assembly.GetManifestResourceStream("MATR.DefaultExtensions.Resources." + name)
            ?? throw new FileNotFoundException("默认扩展资料缺失：" + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
