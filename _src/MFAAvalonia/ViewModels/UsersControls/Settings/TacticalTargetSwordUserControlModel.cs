using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace MFAAvalonia.ViewModels.UsersControls.Settings;

/// <summary>当前战术强化任务的目标刀剑选择，复用掉落播报的搜索候选与标签样式。</summary>
public partial class TacticalTargetSwordUserControlModel : ViewModelBase
{
    private readonly MaaInterface.MaaInterfaceSelectOption _option;
    private readonly Action _save;
    private readonly List<SwordCatalogEntry> _catalog;
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);

    public ObservableCollection<SwordDropNotificationTagItem> Tags { get; } = [];
    public ObservableCollection<SwordDropNotificationCandidateItem> Candidates { get; } = [];
    [ObservableProperty] private string _searchText = string.Empty;

    public TacticalTargetSwordUserControlModel(MaaInterface.MaaInterfaceSelectOption option, Action save,
        IEnumerable<SwordCatalogEntry>? catalog = null)
    {
        _option = option;
        _save = save;
        _catalog = catalog?.ToList() ?? SwordCatalogService.Load(
            Path.Combine(AppPaths.ResourceDirectory, "base", "SwordBookCatalog.json"));
        foreach (var name in TacticalTargetSwordSettings.ReadNames(option))
            _selected.Add(name);
        RefreshTags();
    }

    public void ActivateSearch() => RefreshCandidates();
    partial void OnSearchTextChanged(string value) => RefreshCandidates();

    private IEnumerable<SwordCatalogEntry> SortedSelected() => _catalog
        .Where(entry => _selected.Contains(entry.BaseName))
        .OrderBy(entry => SwordCatalogService.TypeRank(entry.Type)).ThenBy(entry => _catalog.IndexOf(entry));

    private void RefreshTags()
    {
        Tags.Clear();
        foreach (var entry in SortedSelected())
            Tags.Add(new SwordDropNotificationTagItem(entry.DisplayName, entry.Type, entry.BaseName, RemoveCommand));
        // 名册暂缺的已保存刀名也保留为可移除标签，避免编辑其它名字时悄悄丢失。
        foreach (var name in _selected.Where(name => _catalog.All(entry => entry.BaseName != name)).Order())
            Tags.Add(new SwordDropNotificationTagItem(name, string.Empty, name, RemoveCommand));
    }

    private void RefreshCandidates()
    {
        Candidates.Clear();
        foreach (var entry in SwordCatalogService.Search(_catalog, SearchText))
            Candidates.Add(new SwordDropNotificationCandidateItem(entry.DisplayName, entry.Type,
                entry.BaseName, _selected.Contains(entry.BaseName), AddCommand));
    }

    [RelayCommand]
    private void Add(string name)
    {
        if (!_catalog.Any(entry => entry.BaseName == name)) return;
        if (!_selected.Add(name)) _selected.Remove(name);
        Persist();
    }

    [RelayCommand]
    private void Remove(string name)
    {
        if (_selected.Remove(name)) Persist();
    }

    private void Persist()
    {
        TacticalTargetSwordSettings.SaveNames(_option, _selected.Order(StringComparer.Ordinal));
        _save();
        RefreshTags();
        RefreshCandidates();
    }
}
