using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MFAAvalonia.Helper;
using MFAAvalonia.Services;
using MFAAvalonia.Views.UserControls.Settings;
using SukiUI.Controls;

namespace MFAAvalonia.Views.Pages;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        DataContext = Instances.SettingsViewModel;
        InitializeComponent();
        // 无扩展时不加入设置项，避免设置目录残留空白入口。
        if (ExtensionContentService.HasContent())
        {
            SettingsLayout.Items.Insert(8, new SettingsLayoutItem
            {
                Header = "扩展设置",
                Content = new ExtensionSettingsUserControl()
            });
        }
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(ForceRelayout);
    }

    private void ForceRelayout()
    {
        SettingsLayout?.InvalidateMeasure();
        SettingsLayout?.InvalidateArrange();
        Settings?.InvalidateMeasure();
        Settings?.InvalidateArrange();
        InvalidateMeasure();
        InvalidateArrange();
    }
}

