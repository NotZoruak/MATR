using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using MFAAvalonia.ViewModels.Pages;

namespace MFAAvalonia.Views.Pages;

/// <summary>工具页面容器。</summary>
public partial class ToolsView : UserControl
{
    public ToolsView()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<ToolsViewModel>();
    }
}
