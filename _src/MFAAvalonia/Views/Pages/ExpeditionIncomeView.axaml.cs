using Avalonia.Controls;
using MFAAvalonia.ViewModels.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace MFAAvalonia.Views.Pages;

public partial class ExpeditionIncomeView : UserControl
{
    public ExpeditionIncomeView()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<DataLookupViewModel>();
    }
}
