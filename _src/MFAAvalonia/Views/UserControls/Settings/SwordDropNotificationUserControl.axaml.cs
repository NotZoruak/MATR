using Avalonia.Controls;
using Avalonia.Input;
using MFAAvalonia.Helper;

namespace MFAAvalonia.Views.UserControls.Settings;

public partial class SwordDropNotificationUserControl : UserControl
{
    public SwordDropNotificationUserControl()
    {
        DataContext = Instances.SwordDropNotificationUserControlModel;
        InitializeComponent();
    }

    private void SearchBox_OnGotFocus(object? sender, GotFocusEventArgs e)
    {
        Instances.SwordDropNotificationUserControlModel.ActivateSearch();
    }
}
