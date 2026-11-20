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

/// <summary>默认部队页面入口，显示五支部队状态和成员详情。</summary>
public sealed class TeamsWorkspace : IExtensionWorkspace
{
    private TeamStatusView? _view;
    private GameOverviewLogParser.Snapshot? _snapshot;

    public Control CreateView(IExtensionContext context)
    {
        _view ??= new TeamStatusView(context.DataDirectory);
        if (_snapshot != null) _view.SetSnapshot(_snapshot);
        return _view;
    }

    public void Update(ExtensionState state)
    {
        _snapshot = string.IsNullOrWhiteSpace(state.SnapshotJson)
            ? null : JsonConvert.DeserializeObject<GameOverviewLogParser.Snapshot>(state.SnapshotJson);
        if (_snapshot == null) _view?.ClearSnapshot(); else _view?.SetSnapshot(_snapshot);
    }

    public void Tick(DateTimeOffset now) => _view?.Tick(now);
    public void Dispose() { _view = null; _snapshot = null; }
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

/// <summary>收获记录入口，按账号保存日志明确给出的奖励事件。</summary>
public sealed class HarvestWorkspace : IExtensionWorkspace
{
    private MFAAvalonia.Views.Pages.HarvestRecordsView? _view;
    private string? _dataDirectory;
    public Control CreateView(IExtensionContext context)
    {
        _dataDirectory = context.DataDirectory;
        _view ??= new MFAAvalonia.Views.Pages.HarvestRecordsView(context.DataDirectory);
        return _view;
    }
    public void Update(ExtensionState state)
    {
        if (_view == null || string.IsNullOrWhiteSpace(state.SnapshotJson)) return;
        var snapshot = JsonConvert.DeserializeObject<GameOverviewLogParser.Snapshot>(state.SnapshotJson);
        if (snapshot != null)
        {
            _view.AddRecords(snapshot.Account, snapshot.HarvestRecords);
            _view.AddDutyRecords(snapshot.Account, snapshot.DutyRecords);
        }
    }
    public void Tick(DateTimeOffset now) { }
    public void Dispose() { _view = null; _dataDirectory = null; }
}
