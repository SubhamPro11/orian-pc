using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Orion.App.Services;
using Orion.App.ViewModels;

namespace Orion.App.Views;

public sealed partial class DownloadsPage : Page
{
    public DownloadsViewModel ViewModel { get; }
    private readonly IAppLogger _logger;

    public DownloadsPage()
    {
        var services = ((App)Microsoft.UI.Xaml.Application.Current).Services;
        ViewModel = services.GetRequiredService<DownloadsViewModel>();
        _logger = services.GetRequiredService<IAppLogger>();
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        try
        {
            ViewModel.RefreshQueue();
        }
        catch (Exception ex)
        {
            _logger.LogError("Error refreshing queue on DownloadsPage", ex);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Dispose();
    }
}
