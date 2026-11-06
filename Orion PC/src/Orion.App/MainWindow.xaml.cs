using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Orion.App.Services;
using Orion.App.ViewModels;
using Orion.App.Views;
using Orion.Core.Models;
using WinRT.Interop;

namespace Orion.App;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _mainViewModel;
    private readonly IAppLogger _logger;
    private AppWindow? _appWindow;

    public MainWindow()
    {
        var services = ((App)Application.Current).Services;
        _mainViewModel = services.GetRequiredService<MainViewModel>();
        _logger = services.GetRequiredService<IAppLogger>();

        InitializeComponent();

        ConfigureTitleBar();
        _mainViewModel.NavigationRequested += OnNavigationRequested;

        // Default navigation to Home (selecting item triggers OnNavSelectionChanged)
        RootNavView.SelectedItem = RootNavView.MenuItems[0];

        // Initialize MainViewModel data loading
        _ = _mainViewModel.InitializeAsync();
    }

    private void ConfigureTitleBar()
    {
        try
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            var hWnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            _appWindow.Title = "Orion Store for PC";

            // Set window and taskbar icon to official Orion icon
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath))
            {
                _appWindow.SetIcon(iconPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to configure custom titlebar", ex);
        }
    }

    private void OnDeviceSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_mainViewModel == null) return;
        try
        {
            if (DeviceSwitcherBox?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
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
        catch (Exception ex)
        {
            _logger?.LogError("Error during device selection change", ex);
        }
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogError("Error during navigation selection change", ex);
        }
    }

    private void OnNavigationRequested(string route)
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogError($"Error navigating to route: {route}", ex);
        }
    }
}
