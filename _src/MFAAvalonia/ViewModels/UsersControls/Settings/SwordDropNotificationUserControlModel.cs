using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MFAAvalonia.Configuration;
using MFAAvalonia.Helper;
using MFAAvalonia.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace MFAAvalonia.ViewModels.UsersControls.Settings;

/// <summary>刀剑掉落播报名单，使用刀剑名册搜索并保存基础名称。</summary>
public partial class SwordDropNotificationUserControlModel : ViewModelBase
{
    private readonly List<SwordCatalogEntry> _catalog;
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);

    public ObservableCollection<SwordDropNotificationTagItem> Tags { get; } = [];
    public ObservableCollection<SwordDropNotificationCandidateItem> Candidates { get; } = [];

    [ObservableProperty] private string _searchText = string.Empty;

    public SwordDropNotificationUserControlModel()
    {
        _catalog = SwordCatalogService.Load(Path.Combine(AppPaths.ResourceDirectory, "base", "SwordBookCatalog.json"));
        LoadList();
    }

    /// <summary>搜索框获得焦点时显示当前搜索结果。</summary>
    public void ActivateSearch() => RefreshCandidates();

    private void LoadList()
    {
        if (ConfigurationManager.Current.TryGetValue(
                ConfigurationKeys.SwordDropNotificationSwords, out List<string>? saved))
        {
            foreach (var name in saved ?? [])
            {
                if (_catalog.Any(entry => entry.BaseName == name))
                    _selected.Add(name);
            }
        }

        RefreshTags();
    }

    partial void OnSearchTextChanged(string value) => RefreshCandidates();

    private IEnumerable<SwordCatalogEntry> SortedSelected() => _catalog
        .Where(entry => _selected.Contains(entry.BaseName))
        .OrderBy(entry => SwordCatalogService.TypeRank(entry.Type))
        .ThenBy(entry => _catalog.IndexOf(entry));

    private void RefreshTags()
    {
        Tags.Clear();
        foreach (var entry in SortedSelected())
            Tags.Add(new SwordDropNotificationTagItem(entry.DisplayName, entry.Type, entry.BaseName, RemoveCommand));
    }

    private void RefreshCandidates()
    {
        Candidates.Clear();
        var keyword = SearchText?.Trim() ?? string.Empty;
        foreach (var entry in SwordCatalogService.Search(_catalog, keyword))
        {
            Candidates.Add(new SwordDropNotificationCandidateItem(entry.DisplayName, entry.Type,
                entry.BaseName, _selected.Contains(entry.BaseName), AddCommand));
        }
    }

    [RelayCommand]
    private void Add(string baseName)
    {
        if (!_selected.Add(baseName))
            _selected.Remove(baseName);
        Persist();
        RefreshTags();
        RefreshCandidates();
    }

    [RelayCommand]
    private void Remove(string baseName)
    {
        if (!_selected.Remove(baseName)) return;
        Persist();
        RefreshTags();
        RefreshCandidates();
    }

    private void Persist() => ConfigurationManager.Current.SetValue(
        ConfigurationKeys.SwordDropNotificationSwords, SortedSelected().Select(entry => entry.BaseName).ToList());
}

public partial class SwordDropNotificationTagItem : ObservableObject
{
    private readonly IRelayCommand<string> _removeCommand;
    public SwordDropNotificationTagItem(string displayName, string type, string baseName, IRelayCommand<string> removeCommand)
    { DisplayName = displayName; Type = type; BaseName = baseName; _removeCommand = removeCommand; }
    public string DisplayName { get; }
    public string Type { get; }
    public string BaseName { get; }
    [RelayCommand] private void Remove() => _removeCommand.Execute(BaseName);
}

public partial class SwordDropNotificationCandidateItem : ObservableObject
{
    private readonly IRelayCommand<string> _addCommand;
    public SwordDropNotificationCandidateItem(string displayName, string type, string baseName, bool isSelected, IRelayCommand<string> addCommand)
    { DisplayName = displayName; Type = type; BaseName = baseName; IsSelected = isSelected; _addCommand = addCommand; }
    public string DisplayName { get; }
    public string Type { get; }
    public string BaseName { get; }
    [ObservableProperty] private bool _isSelected;
    [RelayCommand] private void Add() => _addCommand.Execute(BaseName);
}
