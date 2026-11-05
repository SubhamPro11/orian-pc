using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orion.App.Services;
using Orion.Core.Models;

namespace Orion.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ICatalogDataService _catalogService;

    [ObservableProperty]
    private PlatformCategory _selectedDevice = PlatformCategory.Windows;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private int _downloadBadgeCount = 0;

    [ObservableProperty]
    private AppItem? _selectedApp;

    public event Action<PlatformCategory>? DeviceCategoryChanged;
    public event Action<AppItem>? AppSelected;
    public event Action<string>? NavigationRequested;

    public MainViewModel(ICatalogDataService catalogService)
    {
        _catalogService = catalogService;
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            await _catalogService.LoadAppsAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedDeviceChanged(PlatformCategory value)
    {
        DeviceCategoryChanged?.Invoke(value);
    }

    [RelayCommand]
    public void SelectApp(AppItem app)
    {
        SelectedApp = app;
        AppSelected?.Invoke(app);
        NavigationRequested?.Invoke("AppDetail");
    }

    [RelayCommand]
    public void Navigate(string route)
    {
        NavigationRequested?.Invoke(route);
    }
}

