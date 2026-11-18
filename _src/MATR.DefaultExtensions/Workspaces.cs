using Avalonia.Controls;
using Avalonia.Layout;
using MATR.ExtensionSdk;
using MFAAvalonia.Services;
using MFAAvalonia.Views.Pages;
using Newtonsoft.Json;

namespace MATR.DefaultExtensions;

/// <summary>默认状态总览入口，仅负责显示宿主交付的快照。</summary>
public sealed class OverviewWorkspace : IExtensionWorkspace
{
    private DefaultOverviewView? _view;
    private ExtensionState? _state;
    public Control CreateView(IExtensionContext context)
    {
        _view ??= new DefaultOverviewView(context);
        if (_state != null) _view.Update(_state);
        return _view;
    }
    public void Update(ExtensionState state) { _state = state; _view?.Update(state); }
    public void Tick(DateTimeOffset now) => _view?.Tick(now);
    public void Dispose() { _view = null; _state = null; }
}

/// <summary>默认部队页面入口，提供尚未实现的提示。</summary>
public sealed class TeamsWorkspace : IExtensionWorkspace
{
    public Control CreateView(IExtensionContext context) => new StackPanel
    {
        Spacing = 12, Margin = new Avalonia.Thickness(24),
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        Children =
        {
            new TextBlock { Text = "部队状况", FontSize = 22 },
            new TextBlock { Text = "部队详情功能正在准备中，可先在状态总览查看部队摘要。", TextWrapping = Avalonia.Media.TextWrapping.Wrap }
        }
    };
    public void Update(ExtensionState state) { }
    public void Tick(DateTimeOffset now) { }
    public void Dispose() { }
}

/// <summary>默认所持刀剑入口，独立管理快照和页面设置。</summary>
public sealed class SwordsWorkspace : IExtensionWorkspace
{
    private HeldSwordsView? _view;
    private GameOverviewLogParser.Snapshot? _snapshot;
    public Control CreateView(IExtensionContext context)
    {
        var layout = Path.Combine(context.DataDirectory, "held-swords-view.json");
        var previous = Path.Combine(MFAAvalonia.Helper.AppPaths.ConfigDirectory, "honmaru", "extension", "held-swords-view.json");
        if (!File.Exists(layout) && File.Exists(previous))
        {
            Directory.CreateDirectory(context.DataDirectory);
            File.Copy(previous, layout, false);
        }
        _view ??= new HeldSwordsView(context.DataDirectory);
        Refresh();
        return _view;
    }
    public void Update(ExtensionState state)
    {
        _snapshot = string.IsNullOrWhiteSpace(state.SnapshotJson) ? null : JsonConvert.DeserializeObject<GameOverviewLogParser.Snapshot>(state.SnapshotJson);
        Refresh();
    }
    private void Refresh()
    {
        if (_snapshot == null) _view?.ClearSnapshot(); else _view?.SetSnapshot(_snapshot);
    }
    public void Tick(DateTimeOffset now) { }
    public void Dispose() { _view = null; _snapshot = null; }
}
