using System;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace MFAAvalonia.Services;

/// <summary>仅在展示时转换活动编号，保留快照和历史记录中的原始编号。</summary>
public static class GameEventNames
{
    private static readonly JObject Catalog = JObject.Parse(MATR.DefaultExtensions.CatalogResources.Read("GameEventCatalog.json"));

    public static string? FormatMap(string? raw)
    {
        if (raw == null) return null;
        var match = Regex.Match(raw, @"^活动 (\d+)-(\d+)$");
        if (!match.Success) return raw;
        var eventId = match.Groups[1].Value;
        var fieldId = match.Groups[2].Value;
        var name = Catalog["events"]?[eventId]?.ToString();
        if (string.IsNullOrWhiteSpace(name)) return raw;
        var field = Catalog["fields"]?[eventId + ":" + fieldId]?.ToString();
        return name + " · " + (string.IsNullOrWhiteSpace(field) ? "关卡 " + fieldId : field);
    }
}
