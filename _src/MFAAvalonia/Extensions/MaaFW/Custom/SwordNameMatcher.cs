using System;
using System.Collections.Generic;
using System.Linq;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>
/// 刀剑、刀装与马匹的名称匹配，供自定编队列表、刀解与合成许可名单、刀剑掉落与内番名条等识别共用：
/// - 刀剑与刀装（IsExactMatch）：原文包含目标即命中；否则把常用日字形、繁体与形近误识字归一为简体后，
///   包含或全等才算命中。一字之差不再放行，避免「太郎太刀/次郎太刀」这类近似名被误选。
///   刀帐目录中只出现在固定刀名里的生僻字（薙、杵）允许整字漏识后再做包含判断。
/// - 马匹（IsHorseMatch）：字形归一并去掉 OCR 文本中的数量标记与名称连接符（中点）后要求与目标全等，
///   不做编辑距离与包含容错，避免「祝一号/祝十号」「白毛/鹿毛/青毛」这类一字之差的马匹名互相误选；
///   仅「高楯黑」允许整字漏识（OCR 读不到「楯」）。
/// - 许可名单（FindMatchedName）与关键词（ContainsName）复用同一套归一规则。
/// 纯文本逻辑、无 MaaFramework 依赖，可被测试工程直接编译。
/// </summary>
public static class SwordNameMatcher
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
        ['銃'] = '铳', ['槍'] = '枪',
        // 常见 OCR 形近误识字（多一笔/少一笔，如「国広」被识别为「国厂」）
        ['厂'] = '广',
        // 刀装「铳兵」的「铳」常被识别为形近的「统」；刀帐目录中不含「统」字，映射不会误伤刀名
        ['统'] = '铳',
        // 「祢祢切丸」的「祢」为生僻字，模型常识别为「称」；刀帐中无含「称」的刀名，映射不会误伤
        ['称'] = '祢',
        // 「蜻蛉切」的「蛉」常被识别为形近的「岭」；刀帐中无含「岭」的刀名，映射不会误伤
        ['岭'] = '蛉',
        // 「平野藤四郎」「骨喰藤四郎」「白山吉光」的首字常被读成形近字；
        // 刀帐中不含「滕」「喻」「百」，映射不会误伤其它刀名
        ['滕'] = '藤',
        ['喻'] = '喰',
        ['百'] = '白',
        // 「北谷菜切」的「菜」常被读成「莱」，「笹贯」的「笹」常被读成「链」；
        // 刀帐中不含「莱」「链」，映射不会误伤其它刀名
        ['莱'] = '菜',
        ['链'] = '笹',
    };

    /// <summary>
    /// OCR 模型字典未收录或容易整字漏识的刀名用字。刀帐中这些字只出现在固定的刀名里，
    /// 去掉后不会与其他刀名冲突，因此允许目标缺字后再做包含判断。
    /// 「薙」：静形薙刀、巴形薙刀会被识别为「静形刀」「巴形刀」；
    /// 「杵」：御手杵会被识别为「御手」。
    /// 「喰」：骨喰藤四郎会被识别为「骨藤四郎」。
    /// 「樋、笹、蛉、髭、麿」：当前 OCR 模型字典未收录。
    /// 「楯」：马匹「高楯黑」会被识别为「高黑」；该字只出现在这条马匹名中，
    /// 刀帐、刀装与宝物名均不含，去掉后不会与其他名称冲突。
    /// </summary>
    private static readonly char[] FrequentlyDroppedGlyphs = ['薙', '杵', '喰', '樋', '笹', '蛉', '髭', '麿', '楯'];

    /// <summary>已确认的完整刀名 OCR 误识别别名，按目标刀名精确对应。</summary>
    private static readonly Dictionary<string, string[]> ConfirmedOcrAliases = new(StringComparer.Ordinal)
    {
        ["二筋樋贞宗"] = ["二筋通贞宗"],
        ["髭切"] = ["琵切"],
    };

    /// <summary>
    /// 缺字后只剩一个字的刀名。仅当整条 OCR 文本恰好是剩余单字时才允许匹配；
    /// 其它目标仍至少保留两个字，避免普通短词发生误匹配。
    /// </summary>
    private static readonly HashSet<string> SingleGlyphDroppedNameTargets = ["笹贯", "髭切"];

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
        var normalizedOcr = Normalize(ocrText);
        var normalizedTarget = Normalize(target);
        if (ConfirmedOcrAliases.TryGetValue(normalizedTarget, out var aliases)
            && aliases.Any(alias => Normalize(alias) == normalizedOcr))
            return true;
        if (normalizedOcr.Length > 0
            && (normalizedOcr.Contains(normalizedTarget, StringComparison.Ordinal)
                || normalizedOcr == normalizedTarget))
            return true;
        // 单字目标（铳、弓、枪、盾等刀装）只做字形归一，不放行丢字容错，
        // 长度判断必须放在归一化之后，否则单字目标永远拿不到形近字容错
        if (target.Length < 2)
            return false;

        var strippedTarget = StripDroppableGlyphs(normalizedTarget);
        if (strippedTarget == normalizedTarget)
            return false;

        if (SingleGlyphDroppedNameTargets.Contains(target))
            return normalizedOcr == strippedTarget;

        return strippedTarget.Length >= 2
            && normalizedOcr.Contains(strippedTarget, StringComparison.Ordinal);
    }

    /// <summary>
    /// 许可名单匹配：返回第一个命中的刀名，未命中返回 null。
    /// 供刀解、合成等按许可名单选刀的动作复用同一套字形归一与生僻字漏识容错。
    /// </summary>
    public static string? FindMatchedName(string? ocrText, IEnumerable<string>? allowedNames)
    {
        if (string.IsNullOrEmpty(ocrText) || allowedNames == null)
            return null;

        foreach (var name in allowedNames)
        {
            if (IsExactMatch(ocrText, name))
                return name;
        }

        return null;
    }

    /// <summary>
    /// 关键词包含匹配：先按原文包含，再做字形归一后包含，只增加形近字容错，不会减少原文命中。
    /// </summary>
    public static bool ContainsName(string? ocrText, string? target)
    {
        if (string.IsNullOrEmpty(ocrText) || string.IsNullOrEmpty(target))
            return false;
        if (ocrText.Contains(target, StringComparison.Ordinal))
            return true;

        var normalizedOcr = Normalize(ocrText);
        var normalizedTarget = Normalize(target);
        return normalizedOcr.Length > 0 && normalizedTarget.Length > 0
            && normalizedOcr.Contains(normalizedTarget, StringComparison.Ordinal);
    }

    /// <summary>去除目标中允许漏识的生僻字，用于漏字容错比较</summary>
    private static string StripDroppableGlyphs(string text)
    {
        if (text.Length == 0 || !text.Any(c => FrequentlyDroppedGlyphs.Contains(c)))
            return text;
        return new string(text.Where(c => !FrequentlyDroppedGlyphs.Contains(c)).ToArray());
    }

    /// <summary>
    /// 马匹匹配：按字形归一表统一字形，并去掉 OCR 文本中的数量标记
    /// （行号前缀与数量后缀，如「03松风」「小云雀x5」「小云雀×5」「小云雀＊5」「小云雀５」）
    /// 与名称连接符（各类中点写法，使「汗血・新春」与「汗血新春」等价），
    /// 之后要求与目标全等。不做包含判断，也不做编辑距离容错：
    /// 「祝一号/祝十号/祝十一号」「白毛/鹿毛/青毛」「超光/超影」都只差一个字，
    /// 任何丢字或近似容错都会让它们互相命中，选中列表中最靠上的错误马匹。
    /// 仅对 FrequentlyDroppedGlyphs 中的字允许整字漏识（当前只有「高楯黑」的「楯」）。
    /// </summary>
    public static bool IsHorseMatch(string? ocrText, string? target)
    {
        if (string.IsNullOrEmpty(ocrText) || string.IsNullOrEmpty(target))
            return false;

        var normalizedOcr = Normalize(RemoveIgnorableCharacters(ocrText));
        var normalizedTarget = Normalize(RemoveIgnorableCharacters(target));
        if (normalizedOcr == normalizedTarget)
            return true;

        var strippedTarget = StripDroppableGlyphs(normalizedTarget);
        return strippedTarget != normalizedTarget
            && strippedTarget.Length >= 2
            && normalizedOcr == strippedTarget;
    }

    /// <summary>
    /// 去除马匹名比较前可忽略的字符：数量标记与名称连接符。
    /// 名称连接符统一剔除，因此「汗血・新春」与「汗血新春」等价；马匹名去掉中点后互不重复，
    /// 不会让「汗血」与「汗血・新春」互相命中（比较仍要求全等）。
    /// </summary>
    private static string RemoveIgnorableCharacters(string text)
        => new(text.Where(c => !IsQuantityMarker(c) && !IsNameConnector(c)).ToArray());

    /// <summary>是否为数量标记字符（ASCII 字母数字、Unicode 数字与常见乘号星号），不能用 char.IsLetter 代替，它对中文字符也返回 true</summary>
    private static bool IsQuantityMarker(char c)
        => char.IsAsciiLetter(c)
            || char.IsDigit(c)
            || c is '×' or '✕' or '✖' or '╳' or '✗' or '＊' or '*';

    /// <summary>是否为名称连接符：各类中点/句点写法，OCR 与手输可能混用，马匹名中只作分隔用</summary>
    private static bool IsNameConnector(char c)
        => c is '・' or '･' or '·' or '•' or '‧' or '∙' or '⋅' or '﹒' or '．' or '.';

    /// <summary>按字形归一表把字符串中的字形替换为简体代表字</summary>
    private static string Normalize(string text)
    {
        if (text.Length == 0) return text;
        return new string(text
            .Where(c => !char.IsWhiteSpace(c))
            .Select(c => GlyphMap.TryGetValue(c, out var mapped) ? mapped : c)
            .ToArray());
    }

}
