using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using MFAAvalonia.Helper;
using MFAAvalonia.Services;
using Newtonsoft.Json;

namespace MFAAvalonia.Views.Pages;

public partial class ExtensionOverviewView
{
    private sealed record PausedPageSnapshot(GameOverviewSnapshot? Snapshot, DateTimeOffset? CollectedAt);
    private readonly HashSet<string> _pausedPages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PausedPageSnapshot> _pausedPageSnapshots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PausedPageSnapshot> _latestPageSnapshots = new(StringComparer.Ordinal);

    private string PausedSnapshotPath(string id) => Path.Combine(
        _configDirectory, "honmaru", "default", "snapshots", "paused-pages", id + ".json");

    private void LoadPausedPages()
    {
        try
        {
            var ids = UiSettings.Read<string[]>("PausedPages") ?? [];
            _pausedPages.UnionWith(ids.Where(id => Entries.Any(entry => entry.Id == id)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            LoggerHelper.Warning("本丸页面停用状态读取失败：" + exception.GetType().Name);
            return;
        }

        foreach (var id in _pausedPages)
        {
            try
            {
                var path = PausedSnapshotPath(id);
                if (File.Exists(path))
                    _pausedPageSnapshots[id] = JsonConvert.DeserializeObject<PausedPageSnapshot>(File.ReadAllText(path))
                        ?? new PausedPageSnapshot(null, null);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                // 读取失败时保持停用，不用最新同步结果替换停用前的数据。
                LoggerHelper.Warning("本丸停用页面快照读取失败：" + exception.GetType().Name);
            }
        }
    }

    private void ConfigurePageContextMenu(ListBoxItem button, Entry entry)
    {
        var toggle = new MenuItem();
        toggle.Click += (_, _) => SetPagePaused(entry.Id, !_pausedPages.Contains(entry.Id));
        var menu = new ContextMenu();
        menu.Items.Add(toggle);
        button.ContextMenu = menu;
        UpdatePageNavigation(button, entry);
    }

    private void UpdatePageNavigation(ListBoxItem button, Entry entry)
    {
        var paused = _pausedPages.Contains(entry.Id);
        button.Classes.Set("inactive", paused);
        if (button.ContextMenu?.Items[0] is MenuItem toggle)
            toggle.Header = paused ? "启用" : "停用";
        ToolTip.SetTip(button, entry.Name + (paused
            ? "（已停用，仅查看保存的数据；右键启用，拖动调整顺序）"
            : "（右键停用，拖动调整顺序）"));
        Avalonia.Automation.AutomationProperties.SetName(button, entry.Name + (paused ? "，已停用" : ""));
    }

    private void SetPagePaused(string id, bool paused)
    {
        try
        {
            if (paused)
            {
                // 先让尚未打开的记录页保存当前结果，再冻结独立副本。
                GetOrCreatePage(id);
                var document = _latestPageSnapshots.GetValueOrDefault(id) ?? new PausedPageSnapshot(null, null);
                var json = JsonConvert.SerializeObject(document);
                var frozen = JsonConvert.DeserializeObject<PausedPageSnapshot>(json)
                    ?? throw new InvalidDataException("停用页面快照无效。");
                WritePageStateFile(PausedSnapshotPath(id), json);
                UiSettings.Write("PausedPages", _pausedPages.Append(id).Distinct().ToArray());
                _pausedPageSnapshots[id] = frozen;
                _pausedPages.Add(id);
            }
            else
            {
                UiSettings.Write("PausedPages", _pausedPages.Where(value => value != id).ToArray());
                _pausedPages.Remove(id);
                _pausedPageSnapshots.Remove(id);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            LoggerHelper.Warning("本丸页面停用状态保存失败：" + exception.GetType().Name);
            ToastHelper.Warn("设置未保存", "请检查本丸数据目录权限。");
            return;
        }

        var entry = Entries.First(value => value.Id == id);
        var button = _navigationItems.First(item => Equals(item.Tag, id));
        UpdatePageNavigation(button, entry);
        if (_views.TryGetValue(id, out var view))
            UpdatePage(id, view, paused ? "已停用同步，显示已保存的数据" : "最近一次读取的账号状态");
    }

    private static void WritePageStateFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, content);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
