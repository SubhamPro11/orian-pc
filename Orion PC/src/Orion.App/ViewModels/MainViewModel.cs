using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Orion.App.Services;
using Orion.Core.Models;
using Orion.Platform.Windows.Services;

namespace Orion.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ICatalogDataService _catalogService;
    private readonly IGitHubAuthService _gitHubAuthService;
    private readonly DispatcherQueue? _dispatcherQueue;

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

    [ObservableProperty]
    private bool _isGitHubAuthenticated;

    [ObservableProperty]
    private GitHubUser? _currentGitHubUser;

    [ObservableProperty]
    private bool _isGitHubBusy;

    public string GitHubUserDisplayName => CurrentGitHubUser?.Name ?? CurrentGitHubUser?.Login ?? string.Empty;
    public string GitHubUserLogin => CurrentGitHubUser != null ? $"@{CurrentGitHubUser.Login}" : string.Empty;
    public string GitHubUserAvatarUrl => CurrentGitHubUser?.AvatarUrl ?? string.Empty;

    public event Action<PlatformCategory>? DeviceCategoryChanged;
    public event Action<AppItem>? AppSelected;
    public event Action<string>? NavigationRequested;

    public MainViewModel(ICatalogDataService catalogService, IGitHubAuthService gitHubAuthService)
    {
        _catalogService = catalogService;
        _gitHubAuthService = gitHubAuthService;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        _isGitHubAuthenticated = _gitHubAuthService.IsAuthenticated;
        _currentGitHubUser = _gitHubAuthService.CurrentUser;

        _gitHubAuthService.AuthStateChanged += OnAuthStateChanged;
    }

    private void OnAuthStateChanged(GitHubUser? user)
    {
        RunOnUIThread(() =>
        {
            CurrentGitHubUser = user;
            IsGitHubAuthenticated = user != null;
            OnPropertyChanged(nameof(GitHubUserDisplayName));
            OnPropertyChanged(nameof(GitHubUserLogin));
            OnPropertyChanged(nameof(GitHubUserAvatarUrl));
        });
    }

    private void RunOnUIThread(Action action)
    {
        if (_dispatcherQueue != null && !_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() => action());
        }
        else
        {
            action();
        }
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

    [RelayCommand]
    public async Task LoginGitHubAsync()
    {
        IsGitHubBusy = true;
        try
        {
            await _gitHubAuthService.LoginWithBrowserAsync();
        }
        finally
        {
            IsGitHubBusy = false;
        }
    }

    [RelayCommand]
    public void LogoutGitHub()
    {
        _gitHubAuthService.Logout();
    }
}
