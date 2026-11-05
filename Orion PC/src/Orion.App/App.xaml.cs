using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Orion.App.Services;
using Orion.App.ViewModels;
using Orion.Core.Services;
using Orion.Platform.Windows.Services;

namespace Orion.App;

public partial class App : Application
{
    private Window? _window;
    public IServiceProvider Services { get; }

    public App()
    {
        InitializeComponent();
        Services = ConfigureServices();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

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
        _window = new MainWindow();
        _window.Activate();
    }
}
