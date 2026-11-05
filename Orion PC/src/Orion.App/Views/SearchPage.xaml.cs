using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Orion.App.ViewModels;
using Orion.Core.Models;

namespace Orion.App.Views;

public sealed partial class SearchPage : Page
{
    public SearchViewModel ViewModel { get; }

    public SearchPage()
    {
        InitializeComponent();
        ViewModel = ((App)Microsoft.UI.Xaml.Application.Current).Services.GetRequiredService<SearchViewModel>();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Initialize();
    }

    private void OnAppItemClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AppItem app)
        {
            ViewModel.OpenApp(app);
        }
    }
}

