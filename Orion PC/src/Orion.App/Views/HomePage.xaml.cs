using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Orion.App.Services;
using Orion.App.ViewModels;
using Orion.Core.Models;

namespace Orion.App.Views;

public sealed partial class HomePage : Page
{
    public HomeViewModel ViewModel { get; }
    private readonly IAppLogger _logger;

    public HomePage()
    {
        var services = ((App)Microsoft.UI.Xaml.Application.Current).Services;
        ViewModel = services.GetRequiredService<HomeViewModel>();
        _logger = services.GetRequiredService<IAppLogger>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        try
        {
            await ViewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to initialize HomePage", ex);
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
            _logger.LogError("Error handling app click on HomePage", ex);
        }
    }
}
