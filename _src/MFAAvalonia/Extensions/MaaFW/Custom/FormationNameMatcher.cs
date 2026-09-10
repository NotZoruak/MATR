using System;
using System.Collections.Generic;
using System.Linq;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>
/// 自定编队列表名称匹配：
/// - 刀剑与刀装（IsExactMatch）：原文包含目标即命中；否则把常用日字形、繁体与形近误识字归一为简体后，
///   包含或全等才算命中。一字之差不再放行，避免「太郎太刀/次郎太刀」这类近似名被误选。
/// - 马匹（IsLegacyFuzzyMatch）：保留原有 OCR 丢字容错（编辑距离 ≤ 1）。
/// 纯文本逻辑、无 MaaFramework 依赖，可被测试工程直接编译。
/// </summary>
public static class FormationNameMatcher
{
    /// <summary>
    /// 字形归一表：键为日文汉字字形、繁体字形或常见 OCR 形近误识字，值为简体代表字；
    /// 比较双方都经过归一化，因此同一字的不同字形视为相等。出现新的字形差异时在此补充，
    /// 只收纳同一字的不同字形，不收不同字（如「鉄砲/铳」这种同物异名不在此列）。
    /// </summary>
    private static readonly Dictionary<char, char> GlyphMap = new()
    {
        // 日文汉字字形 / 繁体 → 简体（刀名刀装用字）
        ['長'] = '长', ['後'] = '后', ['國'] = '国', ['広'] = '广', ['廣'] = '广',
        ['義'] = '义', ['貫'] = '贯', ['亀'] = '龟', ['貞'] = '贞', ['愛'] = '爱',
        ['徹'] = '彻', ['薬'] = '药', ['竜'] = '龙', ['鶴'] = '鹤', ['蛍'] = '萤',
        ['極'] = '极', ['伝'] = '传', ['撃'] = '击', ['鈴'] = '铃', ['児'] = '儿',
        ['歳'] = '岁', ['剣'] = '剑', ['巻'] = '卷', ['飾'] = '饰', ['雑'] = '杂',
        ['沢'] = '泽', ['瀬'] = '濑', ['岡'] = '冈', ['島'] = '岛', ['軽'] = '轻',
        ['歩'] = '步', ['騎'] = '骑', ['鋭'] = '锐', ['鐵'] = '铁', ['鉄'] = '铁',
        ['曽'] = '曾', ['倶'] = '俱', ['鳴'] = '鸣', ['壓'] = '压', ['鐘'] = '钟',
        ['雲'] = '云', ['黒'] = '黑', ['豊'] = '丰',
        // 常见 OCR 形近误识字（多一笔/少一笔，如「国広」被识别为「国厂」）
        ['厂'] = '广',
        // 「祢祢切丸」的「祢」为生僻字，模型常识别为「称」；刀帐中无含「称」的刀名，映射不会误伤
        ['称'] = '祢',
    };

    /// <summary>
    /// OCR 容易整字漏识的生僻字。刀帐中这些字只出现在固定的刀名里，
    /// 去掉后不会与其他刀名冲突，因此允许目标缺字后再做包含判断。
    /// 当前仅「薙」：静形薙刀、巴形薙刀会被识别为「静形刀」「巴形刀」。
    /// </summary>
    private static readonly char[] FrequentlyDroppedGlyphs = ['薙'];

    /// <summary>
    /// 刀剑与刀装匹配：原文包含目标即命中；否则归一化字形后包含或全等才算命中。
    /// 仅对 FrequentlyDroppedGlyphs 中的生僻字允许整字缺失，其余情况不做丢字容错，
    /// 避免「太郎太刀/次郎太刀」这类近似名被误选。
    /// </summary>
    public static bool IsExactMatch(string? ocrText, string? target)
    {
        if (string.IsNullOrEmpty(ocrText) || string.IsNullOrEmpty(target))
            return false;
        if (ocrText.Contains(target, StringComparison.Ordinal))
            return true;
        if (target.Length < 2)
            return false;
        var normalizedOcr = Normalize(ocrText);
        var normalizedTarget = Normalize(target);
        if (normalizedOcr.Length > 0
            && (normalizedOcr.Contains(normalizedTarget, StringComparison.Ordinal)
                || normalizedOcr == normalizedTarget))
            return true;

        var strippedTarget = StripDroppableGlyphs(normalizedTarget);
        return strippedTarget.Length >= 2
            && strippedTarget != normalizedTarget
            && normalizedOcr.Contains(strippedTarget, StringComparison.Ordinal);
    }

    /// <summary>去除目标中允许漏识的生僻字，用于漏字容错比较</summary>
    private static string StripDroppableGlyphs(string text)
    {
        if (text.Length == 0 || !text.Any(c => FrequentlyDroppedGlyphs.Contains(c)))
            return text;
        return new string(text.Where(c => !FrequentlyDroppedGlyphs.Contains(c)).ToArray());
    }

    /// <summary>马匹匹配：保留原有容错——原文包含目标；或目标 ≥ 2 字时，去除 OCR 文本中的数字/字母后与目标编辑距离 ≤ 1。</summary>
    public static bool IsLegacyFuzzyMatch(string? ocrText, string? target)
    {
        if (string.IsNullOrEmpty(ocrText) || string.IsNullOrEmpty(target))
            return false;
        if (ocrText.Contains(target, StringComparison.Ordinal))
            return true;
        if (target.Length < 2)
            return false;
        // 去除 OCR 文本中的 ASCII 数字前缀（如「05」）与数量后缀（如「x1」）；不能用 char.IsLetter，它对中文字符也返回 true
        var cleaned = new string(ocrText.Where(c => !char.IsAsciiLetterOrDigit(c)).ToArray());
        return cleaned.Length > 0 && LevenshteinDistance(cleaned, target) <= 1;
    }

    /// <summary>按字形归一表把字符串中的字形替换为简体代表字</summary>
    private static string Normalize(string text)
    {
        if (text.Length == 0) return text;
        return new string(text.Select(c => GlyphMap.TryGetValue(c, out var mapped) ? mapped : c).ToArray());
    }

    /// <summary>计算两个短字符串的编辑距离（Levenshtein）</summary>
    private static int LevenshteinDistance(string a, string b)
    {
        int m = a.Length, n = b.Length;
        if (m == 0) return n;
        if (n == 0) return m;
        var dp = new int[m + 1, n + 1];
        for (int i = 0; i <= m; i++) dp[i, 0] = i;
        for (int j = 0; j <= n; j++) dp[0, j] = j;
        for (int i = 1; i <= m; i++)
            for (int j = 1; j <= n; j++)
                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                    dp[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return dp[m, n];
    }
}
