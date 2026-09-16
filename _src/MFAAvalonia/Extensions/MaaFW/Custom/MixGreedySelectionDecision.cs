using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>提供习合葛朗台选材所需的纯计算与坐标数据。</summary>
public static class MixGreedySelectionDecision
{
    /// <summary>手动选择单个素材时的最大点击次数。</summary>
    public static int ManualClickAttempts => 2;

    /// <summary>点击全部解除后等待界面刷新的时长。</summary>
    public static int ClearAllDelayMilliseconds => 500;

    /// <summary>全部解除的最大点击次数。</summary>
    public static int ClearAllAttempts => 2;

    /// <summary>滑动结束后等待列表停止惯性滚动的时长。</summary>
    public static int SwipeSettleDelayMilliseconds => 500;

    /// <summary>下一级需求数字未识别时使用的备用 OCR 区域。</summary>
    public static MixGreedyRectangle FallbackNeedOcrRoi => new(431, 342, 15, 17);

    private static readonly int[][] MaterialNeeds =
    [
        [],
        [1, 8, 8, 8, 16, 16],
        [1, 7, 7, 7, 15, 15],
        [1, 5, 5, 5, 11, 11],
        [1, 3, 3, 3, 7, 7],
        [1, 2, 2, 2, 5, 5]
    ];

    /// <summary>当前页五个完整素材行的取消点击位置。</summary>
    public static readonly MixGreedyPoint[] CancelPositions =
    [
        new(857, 212),
        new(858, 313),
        new(858, 414),
        new(858, 516),
        new(858, 617)
    ];

    /// <summary>按详情页稀有度像素读取稀有度。</summary>
    public static bool TryGetRarity(byte red, byte green, byte blue, out int rarity)
    {
        rarity = (red, green, blue) switch
        {
            (90, 90, 90) => 1,
            (178, 174, 172) => 2,
            (109, 86, 32) => 3,
            (186, 160, 85) => 4,
            (101, 70, 23) => 5,
            _ => 0
        };
        return rarity != 0;
    }

    /// <summary>计算当前刀剑升至乱舞7级仍需的素材数量。</summary>
    public static int CalculateRequiredMaterialCount(int rarity, int level, int needForNextLevel)
    {
        if (rarity is < 1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(rarity));
        if (level is < 1 or > 6)
            throw new ArgumentOutOfRangeException(nameof(level));
        if (needForNextLevel <= 0)
            throw new ArgumentOutOfRangeException(nameof(needForNextLevel));

        var required = needForNextLevel;
        for (var targetLevel = level + 1; targetLevel <= 6; targetLevel++)
            required += MaterialNeeds[rarity][targetLevel - 1];
        return required;
    }

    /// <summary>从“已选数量/30”形式的 OCR 结果中读取已选数量。</summary>
    public static bool TryParseSelectedCount(string? text, out int selectedCount)
    {
        selectedCount = 0;
        var match = Regex.Match(text ?? string.Empty, @"(?<count>[0-9０-９]+)\s*[/／∕]");
        if (!match.Success)
            return false;

        var normalized = string.Concat(match.Groups["count"].Value.Select(NormalizeDigit));
        return int.TryParse(normalized, out selectedCount) && selectedCount is >= 0 and <= 30;
    }

    /// <summary>计算一键选择后已选素材与需求量的差值。</summary>
    public static int GetCancelCount(int requiredCount, int selectedCount) => selectedCount - requiredCount;

    /// <summary>根据需求量与当前已选数量创建后续选材计划。</summary>
    public static MixMaterialSelectionPlan CreatePlan(int requiredCount, int selectedCount) =>
        new(requiredCount, selectedCount);

    /// <summary>判断超额素材是否应采用全部解除后重选的方式处理。</summary>
    public static bool ShouldClearAllSelection(int cancelCount) => cancelCount > 15;

    private static char NormalizeDigit(char value) =>
        value is >= '０' and <= '９' ? (char)('0' + value - '０') : value;
}

/// <summary>屏幕中的一个像素坐标。</summary>
public readonly record struct MixGreedyPoint(int X, int Y);

/// <summary>屏幕中的一个矩形区域。</summary>
public readonly record struct MixGreedyRectangle(int X, int Y, int Width, int Height);

/// <summary>习合素材选择的后续执行方式。</summary>
public enum MixMaterialSelectionMode
{
    Proceed,
    CancelExcess,
    ClearAndReselect
}

/// <summary>将选材数量计算结果表达为可执行的强类型计划。</summary>
public readonly record struct MixMaterialSelectionPlan(int RequiredCount, int SelectedCount)
{
    /// <summary>已选数量减去需求数量，负数表示素材不足。</summary>
    public int Difference => SelectedCount - RequiredCount;

    /// <summary>需要逐把取消的超额素材数量。</summary>
    public int ExcessCount => Math.Max(Difference, 0);

    /// <summary>根据数量差选择后续执行方式。</summary>
    public MixMaterialSelectionMode Mode => Difference switch
    {
        <= 0 => MixMaterialSelectionMode.Proceed,
        > 15 => MixMaterialSelectionMode.ClearAndReselect,
        _ => MixMaterialSelectionMode.CancelExcess
    };
}
