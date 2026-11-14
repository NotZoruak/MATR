using CommunityToolkit.Mvvm.ComponentModel;

namespace MFAAvalonia.ViewModels.Pages;

/// <summary>工具箱页面容器：统一承载计算、识别与资料查询功能。</summary>
public partial class ToolsViewModel : ViewModelBase
{
    /// <summary>当前选中的工具页签，默认打开限锻计算。</summary>
    [ObservableProperty]
    private int _selectedTabIndex;
}
