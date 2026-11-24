using System;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace MFAAvalonia.Services;

/// <summary>按客户端奖励类型与编号查找名称，避免跨类型编号冲突。</summary>
public static class GameItemNames
{
    private static readonly JObject Catalog = Load();

    private static JObject Load()
    {
        using var stream = typeof(GameItemNames).Assembly.GetManifestResourceStream("MFAAvalonia.GameItemCatalog.json")
            ?? throw new InvalidOperationException("缺少客户端物品名称库");
        using var reader = new StreamReader(stream);
        return JObject.Parse(reader.ReadToEnd());
    }

    public static string? Find(string? type, string? id) => type == null ? null
        : Catalog["typeNames"]?[type]?.ToString()
          ?? (id == null ? null : Catalog["names"]?[ $"{type}:{id}"]?.ToString());

    public static string DisplayName(string name)
    {
        var match = Regex.Match(name, @"^未知物品（类型 (\d+)，ID (\d+)）$", RegexOptions.CultureInvariant);
        return match.Success ? Find(match.Groups[1].Value, match.Groups[2].Value) ?? name : name;
    }
}
