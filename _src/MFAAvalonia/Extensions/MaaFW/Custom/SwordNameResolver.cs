using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>
/// 刀种刀名名条解析：把名条 OCR 文本归一为刀帐目录里的唯一标准刀名，
/// 供刀剑掉落对话、内番对话等按名条识别刀剑的动作共用。
/// 匹配分三级放宽：按刀种切分后同类唯一匹配 / 剥离生僻字后包含 / 整行在全表唯一匹配。
/// 能解析出刀种时用刀帐刀种交叉校验，解析不出时（薙刀的薙会被整字漏识）跳过这一步；
/// 任何一级只要出现多个候选就继续降级，最终仍不唯一就返回失败，不做猜测。
/// 纯文本逻辑、无 MaaFramework 依赖，可被测试工程直接编译。
/// </summary>
public static class SwordNameResolver
{
    /// <summary>名条中的刀种前缀，长刀种在前，避免「大太刀」被「太刀」抢先匹配。</summary>
    private static readonly string[] SwordTypes =
    [
        "大太刀", "短刀", "胁差", "打刀", "太刀", "薙刀", "枪", "剑"
    ];

    /// <summary>
    /// 解析名条文本。刀种可解析时只在同类刀剑中匹配；刀种被读残或漏识时改用整行文本在刀帐全表匹配。
    /// </summary>
    public static bool TryResolve(string text, IReadOnlyDictionary<string, string> swordTypeMap, out string swordName)
    {
        swordName = string.Empty;
        var normalized = Regex.Replace(text ?? string.Empty, @"\s+", string.Empty);
        if (normalized.Length == 0 || swordTypeMap.Count == 0)
            return false;

        var swordType = SwordTypes.FirstOrDefault(normalized.StartsWith);
        if (swordType != null && normalized.Length - swordType.Length >= 2)
        {
            var recognizedName = normalized[swordType.Length..];
            var typeCandidates = swordTypeMap
                .Where(pair => string.Equals(pair.Value, swordType, StringComparison.Ordinal))
                .Select(pair => pair.Key)
                .Where(candidate => SwordNameMatcher.IsExactMatch(recognizedName, candidate))
                .ToList();
            if (typeCandidates.Count == 1)
            {
                swordName = typeCandidates[0];
                return true;
            }
        }

        var lineCandidates = swordTypeMap.Keys
            .Where(candidate => SwordNameMatcher.IsExactMatch(normalized, candidate))
            .ToList();
        if (lineCandidates.Count == 1)
        {
            swordName = lineCandidates[0];
            return true;
        }

        return false;
    }
}
