using System;

namespace MFAAvalonia.Extensions.MaaFW;

/// <summary>
/// 常驻作战轮数迁移：把旧「异去_重复次数」下级选项里的次数搬到任务级重复次数。
///
/// 合战场原先把轮数放在「过去/异去」的下级选项，任务本身 repeatable 为 false，
/// 界面从未提供过任务级次数控件，因此旧配置里的重复次数恒为接口默认值，迁移时可以直接覆盖。
/// </summary>
public static class SortieRepeatCountMigration
{
    /// <summary>参与迁移的任务入口。</summary>
    public const string SortieEntry = "Sortie";

    /// <summary>
    /// 解析旧配置里的轮数，判断是否可以迁移到任务级重复次数。
    /// </summary>
    /// <param name="entryName">任务入口名，仅合战场参与迁移。</param>
    /// <param name="legacyRepeatCount">旧下级选项里保存的原始文本。</param>
    /// <param name="repeatCount">迁移后的任务级重复次数。</param>
    /// <returns>是否迁移成功。</returns>
    public static bool TryResolve(string? entryName, string? legacyRepeatCount, out int repeatCount)
    {
        repeatCount = 0;
        if (!string.Equals(entryName, SortieEntry, StringComparison.Ordinal))
            return false;

        if (!int.TryParse(legacyRepeatCount, out var value))
            return false;

        repeatCount = value;
        return true;
    }
}
