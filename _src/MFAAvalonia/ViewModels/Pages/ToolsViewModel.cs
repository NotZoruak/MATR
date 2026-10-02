using CommunityToolkit.Mvvm.ComponentModel;

namespace MFAAvalonia.ViewModels.Pages;

/// <summary>工具页面容器：承载会主动执行识别或计算的辅助功能。</summary>
public partial class ToolsViewModel : ViewModelBase
{
    /// <summary>当前选中的工具页签，默认打开限锻计算。</summary>
    [ObservableProperty]
    private int _selectedTabIndex;
}
