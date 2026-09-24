using System;
using System.Collections.Generic;
using System.Linq;

namespace MFAAvalonia.Services;

/// <summary>管理核心资源对比图中的资源选择。</summary>
public sealed class WarehouseComparisonResourceSelection
{
    public static IReadOnlyList<string> AvailableResourceNames { get; } =
        ["木炭", "玉钢", "冷却材", "砥石"];

    private readonly HashSet<string> _selectedResourceNames =
        new(AvailableResourceNames, StringComparer.Ordinal);

    public IReadOnlyList<string> SelectedResourceNames =>
        AvailableResourceNames.Where(_selectedResourceNames.Contains).ToArray();

    /// <summary>切换资源；至少保留一个资源处于选中状态。</summary>
    public bool Toggle(string resourceName)
    {
        if (!AvailableResourceNames.Contains(resourceName, StringComparer.Ordinal))
            return false;

        if (_selectedResourceNames.Contains(resourceName))
        {
            if (_selectedResourceNames.Count == 1)
                return false;

            _selectedResourceNames.Remove(resourceName);
        }
        else
        {
            _selectedResourceNames.Add(resourceName);
        }

        return true;
    }
}
