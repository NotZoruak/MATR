using Avalonia.Controls;
using MFAAvalonia.ViewModels.UsersControls.Settings;

namespace MFAAvalonia.Views.UserControls.Settings;

/// <summary>扩展的读取与仓库同步设置。</summary>
public partial class ExtensionSettingsUserControl : UserControl
{
    public ExtensionSettingsUserControl()
    {
        InitializeComponent();
        DataContext = new ExtensionSettingsViewModel();
    }
}
