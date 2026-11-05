using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Orion.App.Services;
using Orion.App.ViewModels;
using Orion.Core.Models;

namespace Orion.App.Views;

public sealed partial class SearchPage : Page
{
    public SearchViewModel ViewModel { get; }
    private readonly IAppLogger _logger;

    public SearchPage()
    {
        var services = ((App)Microsoft.UI.Xaml.Application.Current).Services;
        ViewModel = services.GetRequiredService<SearchViewModel>();
        _logger = services.GetRequiredService<IAppLogger>();
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        try
        {
            ViewModel.Initialize();
        }
        catch (Exception ex)
        {
            _logger.LogError("Error initializing SearchPage", ex);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Dispose();
    }

    private void OnAppItemClicked(object sender, ItemClickEventArgs e)
    {
        try
        {
            if (e.ClickedItem is AppItem app)
            {
                ViewModel.OpenApp(app);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Error opening app from SearchPage", ex);
        }
    }
}
