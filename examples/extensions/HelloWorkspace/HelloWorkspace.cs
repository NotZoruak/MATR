using Avalonia.Controls;
using MATR.ExtensionSdk;

namespace ExampleExtensions;

/// <summary>仅依赖公共 SDK 的独立页面，证明宿主无需预设页面类型。</summary>
public sealed class HelloWorkspace : IExtensionWorkspace
{
    private readonly TextBlock _status = new() { Text = "等待同步", Margin = new Avalonia.Thickness(24), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    public Control CreateView(IExtensionContext context) => _status;
    public void Update(ExtensionState state) => _status.Text = "这是扩展包提供的独立界面。\n数据状态：" + state.Status;
    public void Tick(DateTimeOffset now) { }
    public void Dispose() { }
}
