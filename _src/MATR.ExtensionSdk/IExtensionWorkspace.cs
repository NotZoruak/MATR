using Avalonia.Controls;

namespace MATR.ExtensionSdk;

/// <summary>版本一的脱敏状态，JSON 不含原始日志或登录凭据。</summary>
public sealed record ExtensionState(string? SnapshotJson, string Status);

/// <summary>宿主向工作区提供的共享接口，所有 UI 回调均在 UI 线程执行。</summary>
public interface IExtensionContext
{
    string DataDirectory { get; }
    string PackageDirectory { get; }
    Task SyncWarehouseAsync();
}

/// <summary>独立工作区入口，每个包须提供一个可无参构造的实现。</summary>
public interface IExtensionWorkspace : IDisposable
{
    Control CreateView(IExtensionContext context);
    void Update(ExtensionState state);
    void Tick(DateTimeOffset now);
}
