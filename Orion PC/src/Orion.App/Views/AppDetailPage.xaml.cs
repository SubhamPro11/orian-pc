using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Orion.App.ViewModels;
using Orion.Core.Models;

namespace Orion.App.Views;

public sealed partial class AppDetailPage : Page
{
    public AppDetailViewModel ViewModel { get; }

    public AppDetailPage()
    {
        InitializeComponent();
        ViewModel = ((App)Application.Current).Services.GetRequiredService<AppDetailViewModel>();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
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

    private void OnBackClicked(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }
}

