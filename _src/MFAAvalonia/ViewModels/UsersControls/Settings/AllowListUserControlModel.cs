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

/// <summary>
/// 刀解/合成许可名单：供日课合成、锻刀前刀解等消耗刀剑功能共同使用的全局名单。
/// 名称只允许来自刀剑名册，上锁刀剑始终排除，普通与极化形态按名称共享许可状态。
/// </summary>
public partial class AllowListUserControlModel : ViewModelBase
{
    /// <summary>首次初始化写入的默认名单，之后不覆盖用户修改。</summary>
    private static readonly string[] DefaultSwords =
    [
        "加州清光", "歌仙兼定", "陆奥守吉行", "山姥切国广", "蜂须贺虎彻",
        "前田藤四郎", "秋田藤四郎", "乱藤四郎", "五虎退", "药研藤四郎", "爱染国俊", "小夜左文字",
        "笑面青江", "鲶尾藤四郎", "骨喰藤四郎", "堀川国广",
    ];

    private readonly List<SwordCatalogEntry> _catalog;
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);

    /// <summary>已选标签列表（按刀种排序）。</summary>
    public ObservableCollection<AllowListTagItem> Tags { get; } = [];

    /// <summary>搜索候选列表（按刀种排序）。</summary>
    public ObservableCollection<AllowListCandidateItem> Candidates { get; } = [];

    [ObservableProperty] private string _searchText = string.Empty;

    public AllowListUserControlModel()
    {
        _catalog = SwordCatalogService.Load(Path.Combine(AppPaths.ResourceDirectory, "base", "SwordBookCatalog.json"));
        LoadAllowList();
    }

    /// <summary>搜索框获得焦点时显示当前搜索结果。</summary>
    public void ActivateSearch() => RefreshCandidates();

    /// <summary>读取许可名单；首次初始化时写入默认名单。</summary>
    private void LoadAllowList()
    {
        if (!ConfigurationManager.Current.TryGetValue(ConfigurationKeys.AllowListSwords, out List<string>? saved))
        {
            saved = DefaultSwords.Where(name => _catalog.Any(entry => entry.BaseName == name)).ToList();
            ConfigurationManager.Current.SetValue(ConfigurationKeys.AllowListSwords, saved);
        }

        foreach (var name in saved ?? [])
        {
            if (_catalog.Any(entry => entry.BaseName == name))
                _selected.Add(name);
        }
        RefreshTags();
    }

    partial void OnSearchTextChanged(string value)
    {
        RefreshCandidates();
    }

    private IEnumerable<SwordCatalogEntry> SortedSelected()
        => _catalog.Where(entry => _selected.Contains(entry.BaseName))
            .OrderBy(entry => SwordCatalogService.TypeRank(entry.Type))
            .ThenBy(entry => _catalog.IndexOf(entry));

    private void RefreshTags()
    {
        Tags.Clear();
        foreach (var entry in SortedSelected())
            Tags.Add(new AllowListTagItem(entry.DisplayName, entry.Type, entry.BaseName, RemoveCommand));
    }

    private void RefreshCandidates()
    {
        Candidates.Clear();
        var keyword = SearchText?.Trim() ?? string.Empty;
        foreach (var entry in SwordCatalogService.Search(_catalog, keyword))
        {
            Candidates.Add(new AllowListCandidateItem(entry.DisplayName, entry.Type,
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
        if (!_selected.Remove(baseName))
            return;
        Persist();
        RefreshTags();
        RefreshCandidates();
    }

    private void Persist()
    {
        ConfigurationManager.Current.SetValue(ConfigurationKeys.AllowListSwords, SortedSelected().Select(entry => entry.BaseName).ToList());
    }
}

/// <summary>许可名单已选标签条目。</summary>
public partial class AllowListTagItem : ObservableObject
{
    private readonly IRelayCommand<string> _removeCommand;

    public AllowListTagItem(string displayName, string type, string baseName, IRelayCommand<string> removeCommand)
    {
        DisplayName = displayName;
        Type = type;
        BaseName = baseName;
        _removeCommand = removeCommand;
    }

    public string DisplayName { get; }
    public string Type { get; }
    public string BaseName { get; }

    [RelayCommand]
    private void Remove() => _removeCommand.Execute(BaseName);
}

/// <summary>许可名单搜索候选条目。</summary>
public partial class AllowListCandidateItem : ObservableObject
{
    private readonly IRelayCommand<string> _addCommand;

    public AllowListCandidateItem(string displayName, string type, string baseName, bool isSelected, IRelayCommand<string> addCommand)
    {
        DisplayName = displayName;
        Type = type;
        BaseName = baseName;
        IsSelected = isSelected;
        _addCommand = addCommand;
    }

    public string DisplayName { get; }
    public string Type { get; }
    public string BaseName { get; }

    [ObservableProperty] private bool _isSelected;

    [RelayCommand]
    private void Add() => _addCommand.Execute(BaseName);
}
