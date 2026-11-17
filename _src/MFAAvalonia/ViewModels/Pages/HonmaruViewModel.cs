using CommunityToolkit.Mvvm.ComponentModel;
using MFAAvalonia.Services;

namespace MFAAvalonia.ViewModels.Pages;

/// <summary>本丸页面容器：承载按安装情况显示的扩展页与现有本丸页签。</summary>
public partial class HonmaruViewModel : ViewModelBase
{
    public bool HasExtensionContent { get; } = ExtensionContentService.HasContent();

    /// <summary>当前选中的页签索引，默认打开仓库。</summary>
    [ObservableProperty]
    private int _selectedTabIndex = 1;
}
