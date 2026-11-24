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
        // 读取器不可用时不显示扩展设置。
        if (GameLogReaderPluginService.IsAvailable)
        {
            SettingsLayout.Items.Insert(8, new SettingsLayoutItem
            {
                Header = "本丸数据设置",
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

