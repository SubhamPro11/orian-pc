using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Orion.App.Services;
using Orion.App.ViewModels;
using Orion.Core.Models;

namespace Orion.App.Views;

public sealed partial class AppDetailPage : Page
{
    public AppDetailViewModel ViewModel { get; }
    private readonly IAppLogger _logger;

    public AppDetailPage()
    {
        var services = ((App)Application.Current).Services;
        ViewModel = services.GetRequiredService<AppDetailViewModel>();
        _logger = services.GetRequiredService<IAppLogger>();
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        try
        {
            ViewModel.ConfirmActionAsync = async (title, message) =>
            {
                var dialog = new ContentDialog
                {
                    Title = title,
                    Content = message,
                    PrimaryButtonText = "Install Anyway",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot
                };
                var res = await dialog.ShowAsync();
                return res == ContentDialogResult.Primary;
            };

            if (e.Parameter is AppItem app)
            {
                ViewModel.SetApp(app);
            }
            else
            {
                var mainVm = ((App)Application.Current).Services.GetRequiredService<MainViewModel>();
                if (mainVm.SelectedApp != null)
                {
                    ViewModel.SetApp(mainVm.SelectedApp);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Error setting app on AppDetailPage", ex);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.ConfirmActionAsync = null;
    }

    private void OnBackClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Error navigating back from AppDetailPage", ex);
        }
    }
}
