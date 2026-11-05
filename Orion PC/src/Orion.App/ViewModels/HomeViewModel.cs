using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orion.App.Services;
using Orion.Core.Models;
using Orion.Core.Services;

namespace Orion.App.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly ICatalogDataService _catalogService;
    private readonly IStorefrontEngine _storefrontEngine;
    private readonly MainViewModel _mainViewModel;

    [ObservableProperty]
    private ObservableCollection<StoreCollection> _collections = new();

    [ObservableProperty]
    private ObservableCollection<AppItem> _heroApps = new();

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string _currentPlatformName = "Windows";

    public HomeViewModel(ICatalogDataService catalogService, IStorefrontEngine storefrontEngine, MainViewModel mainViewModel)
    {
        _catalogService = catalogService;
        _storefrontEngine = storefrontEngine;
        _mainViewModel = mainViewModel;

        _mainViewModel.DeviceCategoryChanged += OnDeviceCategoryChanged;
    }

    private void OnDeviceCategoryChanged(PlatformCategory category)
    {
        CurrentPlatformName = category switch
        {
            PlatformCategory.Windows => "Windows",
            PlatformCategory.Android => "Android",
            PlatformCategory.Linux => "Linux",
            PlatformCategory.MacOS => "macOS",
            _ => "All Platforms"
        };
        _ = RefreshCollectionsAsync();
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            await _catalogService.LoadAppsAsync();
            await RefreshCollectionsAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    public Task RefreshCollectionsAsync()
    {
        var category = _mainViewModel.SelectedDevice;
        var apps = _catalogService.FilterAppsByPlatform(category);

        var platformStr = category switch
        {
            PlatformCategory.Windows => PlatformConstants.PC,
            PlatformCategory.Android => PlatformConstants.Android,
            PlatformCategory.Linux => PlatformConstants.Linux,
            PlatformCategory.MacOS => PlatformConstants.MacOS,
            _ => "all"
        };

        var cols = _storefrontEngine.BuildStorefront(apps, platformStr);

        Collections.Clear();
        foreach (var col in cols)
        {
            Collections.Add(col);
        }

        HeroApps.Clear();
        var hero = cols.FirstOrDefault(c => c.Type == CollectionType.Hero);
        if (hero != null)
        {
            foreach (var a in hero.AppsList) HeroApps.Add(a);
        }

        return Task.CompletedTask;
    }

    [RelayCommand]
    public void OpenApp(AppItem app)
    {
        _mainViewModel.SelectApp(app);
    }
}

