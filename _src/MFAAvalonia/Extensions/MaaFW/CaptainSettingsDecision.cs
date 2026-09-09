using System.Collections.Generic;

namespace MFAAvalonia.Extensions.MaaFW;

/// <summary>
/// 按任务配置换队长时使用的固定映射与位置转换。
/// </summary>
public static class CaptainSettingsDecision
{
    /// <summary>
    /// 获取任务入口对应的拖拽 action node 名称。
    /// </summary>
    public static string? GetDragNodeName(string? entry) => entry switch
    {
        "Sortie" => "S_DragCaptain",
        "Underground" => "U_DragCaptain",
        "LRentaisen" => "LR_DragCaptain",
        "Hanapai" => "HP_DragCaptain",
        "EdoCastle" => "EC_DragCaptain",
        "TacticalTraining" => "TT_DragCaptain",
        _ => null
    };

    /// <summary>
    /// 获取任务入口对应的跳过位置 option 名称。战术强化不提供该配置。
    /// </summary>
    public static string? GetSkipOptionName(string? entry) => entry switch
    {
        "Sortie" => "S_跳过位置",
        "Underground" => "U_跳过位置",
        "LRentaisen" => "LR_跳过位置",
        "Hanapai" => "HP_跳过位置",
        "EdoCastle" => "EC_跳过位置",
        _ => null
    };

    /// <summary>
    /// 将位置名称转换为零基索引，忽略空值、未知名称和重复项。
    /// </summary>
    public static HashSet<int> ParseSkipPositions(IEnumerable<string>? positionNames)
    {
        var positions = new HashSet<int>();
        if (positionNames == null)
            return positions;

        foreach (var name in positionNames)
        {
            var position = name switch
            {
                "位置一" => 0,
                "位置二" => 1,
                "位置三" => 2,
                "位置四" => 3,
                "位置五" => 4,
                "位置六" => 5,
                _ => -1
            };

            if (position >= 0)
                positions.Add(position);
        }

        return positions;
    }
}
