using Avalonia.Controls;
using Avalonia.Input;
using MFAAvalonia.ViewModels.UsersControls.Settings;

namespace MFAAvalonia.Views.UserControls.Settings;

public partial class TacticalTargetSwordUserControl : UserControl
{
    public TacticalTargetSwordUserControl() => InitializeComponent();

    public TacticalTargetSwordUserControl(TacticalTargetSwordUserControlModel model) : this()
        => DataContext = model;

    private void SearchBox_OnGotFocus(object? sender, GotFocusEventArgs e)
        => (DataContext as TacticalTargetSwordUserControlModel)?.ActivateSearch();
}
