using Avalonia.Controls;
using MFAAvalonia.ViewModels.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace MFAAvalonia.Views.Pages;

public partial class ExperienceTableView : UserControl
{
    public ExperienceTableView()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<DataLookupViewModel>();
    }
}
