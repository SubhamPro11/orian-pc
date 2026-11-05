using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Orion.App.Services;
using Orion.App.ViewModels;
using Orion.Core.Services;
using Orion.Platform.Windows.Services;

namespace Orion.App;

public partial class App : Application
{
    private Window? _window;
    public IServiceProvider Services { get; }
    private readonly FileLogger _logger;

    public App()
    {
        InitializeComponent();
        _logger = new FileLogger();

        // 1. Global XAML Unhandled Exception Handler
        UnhandledException += OnUnhandledException;

        // 2. Global AppDomain Unhandled Exception Handler
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;

        // 3. Unobserved Task Exceptions
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        Services = ConfigureServices(_logger);
        _logger.LogInfo("Orion Store for PC initialized.");
    }

    private static ServiceProvider ConfigureServices(FileLogger logger)
    {
        var services = new ServiceCollection();

        // Logging
        services.AddSingleton<IAppLogger>(logger);

        // HTTP
        services.AddSingleton(new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
            DefaultRequestHeaders = { { "User-Agent", "OrionStorePC/2.6.5" } }
        });

        // Core Services
        services.AddSingleton<IVersionService, VersionService>();
        services.AddSingleton<IAssetResolver, AssetResolver>();
        services.AddSingleton<IHashVerifier, HashVerifier>();
        services.AddSingleton<ICatalogProcessor, CatalogProcessor>();
        services.AddSingleton<IStorefrontEngine, StorefrontEngine>();
        services.AddSingleton<IXpEngine, XpEngine>();
        services.AddSingleton<IReleaseNotesCleaner, ReleaseNotesCleaner>();

        // Platform Services
        services.AddSingleton<IInstalledAppDetector, WindowsAppTracker>();
        services.AddSingleton<IWindowsInstallerService, WindowsInstallerService>();
        services.AddSingleton<IDownloadManager, DownloadManager>();
        services.AddSingleton<ISecureStorageService, WindowsSecureStorage>();
        services.AddSingleton<IVirusTotalService, VirusTotalService>();

        // App Services
        services.AddSingleton<ICatalogDataService, CatalogDataService>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddTransient<HomeViewModel>();
        services.AddTransient<SearchViewModel>();
        services.AddTransient<AppDetailViewModel>();
        services.AddTransient<MyAppsViewModel>();
        services.AddTransient<DownloadsViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<AboutViewModel>();

        return services.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _logger.LogInfo("Application launching MainWindow.");
        _window = new MainWindow();
        _window.Activate();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        _logger.LogError($"XAML UnhandledException: {e.Message}", e.Exception);
        // Mark as handled to prevent sudden crash if recoverable, but log clearly
        e.Handled = true;

        TryShowErrorDialog("An unexpected application error occurred. Details have been saved to the log file.", e.Exception?.Message);
    }

    private void OnAppDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        _logger.LogError($"AppDomain UnhandledException (IsTerminating={e.IsTerminating})", ex);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger.LogError("TaskScheduler UnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    private void TryShowErrorDialog(string title, string? details)
    {
        try
        {
            if (_window?.Content?.XamlRoot != null)
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = _window.Content.XamlRoot,
                    Title = "Application Notice",
                    Content = string.IsNullOrEmpty(details) ? title : $"{title}\n\nError: {details}",
                    CloseButtonText = "OK"
                };
                _ = dialog.ShowAsync();
            }
        }
        catch
        {
            // Do not crash inside error dialog display
        }
    }
}
