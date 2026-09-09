using MFAAvalonia.Services;
using System;
using System.Collections.Generic;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>自动内番在一次运行中固化待安排刀剑，避免流程中读取到变化的刀帐数据。</summary>
public static class NaibanOutfitSelectionContext
{
    private static IReadOnlyList<NaibanOutfitTarget> _targets = [];

    /// <summary>保存本轮自动内番优先安排的刀剑。</summary>
    public static void SetTargets(IReadOnlyList<NaibanOutfitTarget> targets) => _targets = targets;

    /// <summary>取得指定位置的优先目标；没有目标时返回 false，由流程选择任意可用刀剑补位。</summary>
    public static bool TryGetTarget(int slot, out NaibanOutfitTarget? target)
    {
        target = slot is >= 1 and <= 2 && _targets.Count >= slot ? _targets[slot - 1] : null;
        return target != null;
    }
}
