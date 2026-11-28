using CommunityToolkit.Mvvm.ComponentModel;

namespace MFAAvalonia.ViewModels.Pages;

/// <summary>工作记录资源编辑器中的临时行。</summary>
public partial class ResourceGainEditItem : ObservableObject
{
    public ResourceGainEditItem(string name, string amount, string count)
    {
        _name = name;
        _amount = amount;
        _count = count;
    }

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _amount;
    [ObservableProperty] private string _count;
}
