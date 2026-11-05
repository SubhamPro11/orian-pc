using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Orion.App.Services;
using Orion.App.ViewModels;

namespace Orion.App.Views;

public sealed partial class MyAppsPage : Page
{
    public MyAppsViewModel ViewModel { get; }
    private readonly IAppLogger _logger;

    public MyAppsPage()
    {
        var services = ((App)Application.Current).Services;
        ViewModel = services.GetRequiredService<MyAppsViewModel>();
        _logger = services.GetRequiredService<IAppLogger>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        try
        {
            await ViewModel.RefreshAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError("Error refreshing MyAppsPage", ex);
        }
    }

    private void OnOpenDetailsClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is Button { Tag: MyAppItemModel model })
            {
                ViewModel.OpenApp(model);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Error opening app details from MyAppsPage", ex);
        }
    }

    private async void OnUninstallClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is Button { Tag: MyAppItemModel model })
            {
                await ViewModel.UninstallAsync(model);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Error uninstalling app from MyAppsPage", ex);
        }
    }
}
