using CommunityToolkit.Mvvm.ComponentModel;

namespace MFAAvalonia.ViewModels.Pages;

/// <summary>本丸页面容器：按仓库、刀帐、工作记录的顺序承载三个固定页签。</summary>
public partial class HonmaruViewModel : ViewModelBase
{
    /// <summary>当前选中的页签索引，默认打开仓库。</summary>
    [ObservableProperty]
    private int _selectedTabIndex;
}
