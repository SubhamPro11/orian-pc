using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Orion.App.ViewModels;

namespace Orion.App.Views;

public sealed partial class MyAppsPage : Page
{
    public MyAppsViewModel ViewModel { get; }

    public MyAppsPage()
    {
        InitializeComponent();
        ViewModel = ((App)Application.Current).Services.GetRequiredService<MyAppsViewModel>();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.RefreshAsync();
    }

    private void OnOpenDetailsClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MyAppItemModel model })
        {
            ViewModel.OpenApp(model);
        }
    }

    private async void OnUninstallClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MyAppItemModel model })
        {
            await ViewModel.UninstallAsync(model);
        }
    }
}

