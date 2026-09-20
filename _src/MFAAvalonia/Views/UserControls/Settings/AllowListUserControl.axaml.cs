using Avalonia.Controls;
using Avalonia.Input;
using MFAAvalonia.Helper;

namespace MFAAvalonia.Views.UserControls.Settings;

public partial class AllowListUserControl : UserControl
{
    public AllowListUserControl()
    {
        DataContext = Instances.AllowListUserControlModel;
        InitializeComponent();
    }

    private void SearchBox_OnGotFocus(object? sender, GotFocusEventArgs e)
    {
        Instances.AllowListUserControlModel.ActivateSearch();
    }
}
