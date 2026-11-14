using System.Reflection;
using Avalonia.Controls;
using MFAAvalonia.Controls;
using MFAAvalonia;
using MFAAvalonia.ViewModels.Pages;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class ToolboxRegistrationTests
{
    [Fact]
    public void ExperienceGridUsesDataGridTheme()
    {
        // 自定义表格必须复用标准表格主题，否则数据和表头都无法呈现。
        var grid = new ChainedScrollDataGrid();
        Assert.Equal(typeof(DataGrid), grid.StyleKey);
    }

    [Fact]
    public void ConfigureViewsAllowsToolboxPagesToShareDataModel()
    {
        // 执行真实启动注册，确保共享模型不会产生重复映射键。
        var services = new ServiceCollection();
        var configure = typeof(App).GetMethod("ConfigureViews",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var result = configure.Invoke(null, new object[] { services });

        Assert.NotNull(result);
        Assert.Single(services, service => service.ServiceType == typeof(DataLookupViewModel));
    }
}
