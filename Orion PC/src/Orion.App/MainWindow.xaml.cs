using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Orion.App.ViewModels;
using Orion.App.Views;
using Orion.Core.Models;
using WinRT.Interop;

namespace Orion.App;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _mainViewModel;
    private AppWindow? _appWindow;

    public MainWindow()
    {
        InitializeComponent();
        _mainViewModel = ((App)Application.Current).Services.GetRequiredService<MainViewModel>();

        ConfigureTitleBar();
        _mainViewModel.NavigationRequested += OnNavigationRequested;

        // Default navigation to Home
        RootNavView.SelectedItem = RootNavView.MenuItems[0];
        ContentFrame.Navigate(typeof(HomePage));
    }

    private void ConfigureTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        var hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.Title = "Orion Store for PC";
    }

    private void OnDeviceSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DeviceSwitcherBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            _mainViewModel.SelectedDevice = tag switch
            {
                "Windows" => PlatformCategory.Windows,
                "Android" => PlatformCategory.Android,
                "Linux" => PlatformCategory.Linux,
                "MacOS" => PlatformCategory.MacOS,
                "All" => PlatformCategory.All,
                _ => PlatformCategory.Windows
            };
        }
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            ContentFrame.Navigate(typeof(SettingsPage));
            return;
        }

        if (args.SelectedItemContainer is NavigationViewItem item && item.Tag is string tag)
        {
            Type? pageType = tag switch
            {
                "Home" => typeof(HomePage),
                "Search" => typeof(SearchPage),
                "MyApps" => typeof(MyAppsPage),
                "Downloads" => typeof(DownloadsPage),
                "About" => typeof(AboutPage),
                _ => typeof(HomePage)
            };

            if (pageType != null)
            {
                ContentFrame.Navigate(pageType);
            }
        }
    }

    private void OnNavigationRequested(string route)
    {
        if (route == "AppDetail")
        {
            ContentFrame.Navigate(typeof(AppDetailPage), _mainViewModel.SelectedApp);
        }
        else if (route == "Home")
        {
            RootNavView.SelectedItem = RootNavView.MenuItems[0];
            ContentFrame.Navigate(typeof(HomePage));
        }
    }
}
