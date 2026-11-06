using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Orion.App.Services;
using Orion.Core.Models;
using Orion.Platform.Windows.Services;

namespace Orion.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISecureStorageService _secureStorage;
    private readonly IGitHubAuthService _gitHubAuthService;
    private readonly DispatcherQueue? _dispatcherQueue;

    [ObservableProperty]
    private string _virusTotalApiKey = string.Empty;

    [ObservableProperty]
    private string _selectedTheme = "Default";

    [ObservableProperty]
    private bool _useMicaBackdrop = true;

    [ObservableProperty]
    private string _saveMessage = string.Empty;

    [ObservableProperty]
    private bool _isGitHubAuthenticated;

    [ObservableProperty]
    private GitHubUser? _currentGitHubUser;

    [ObservableProperty]
    private bool _isGitHubBusy;

    [ObservableProperty]
    private string _manualToken = string.Empty;

    [ObservableProperty]
    private string _gitHubAuthMessage = string.Empty;

    public string GitHubUserDisplayName => CurrentGitHubUser?.Name ?? CurrentGitHubUser?.Login ?? string.Empty;
    public string GitHubUserLogin => CurrentGitHubUser != null ? $"@{CurrentGitHubUser.Login}" : string.Empty;
    public string GitHubUserAvatarUrl => CurrentGitHubUser?.AvatarUrl ?? string.Empty;
    public string GitHubUserBio => CurrentGitHubUser?.Bio ?? string.Empty;
    public string GitHubUserReposCount => CurrentGitHubUser != null ? $"{CurrentGitHubUser.PublicRepos} public repos" : string.Empty;

    public SettingsViewModel(ISecureStorageService secureStorage, IGitHubAuthService gitHubAuthService)
    {
        _secureStorage = secureStorage;
        _gitHubAuthService = gitHubAuthService;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        _virusTotalApiKey = _secureStorage.GetSecret("VirusTotalApiKey") ?? string.Empty;
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
            OnPropertyChanged(nameof(GitHubUserBio));
            OnPropertyChanged(nameof(GitHubUserReposCount));
            if (user != null)
            {
                GitHubAuthMessage = $"Connected as @{user.Login}";
            }
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

    [RelayCommand]
    public void SaveApiKey()
    {
        if (string.IsNullOrWhiteSpace(VirusTotalApiKey))
        {
            _secureStorage.RemoveSecret("VirusTotalApiKey");
            SaveMessage = "API key cleared.";
        }
        else
        {
            _secureStorage.SetSecret("VirusTotalApiKey", VirusTotalApiKey.Trim());
            SaveMessage = "VirusTotal API key securely saved in Windows Credential Locker.";
        }
    }

    [RelayCommand]
    public async Task LoginGitHubBrowserAsync()
    {
        IsGitHubBusy = true;
        GitHubAuthMessage = "Opening browser for GitHub sign-in...";
        try
        {
            var success = await _gitHubAuthService.LoginWithBrowserAsync();
            if (success)
            {
                GitHubAuthMessage = "Signed in successfully!";
            }
            else
            {
                GitHubAuthMessage = _gitHubAuthService.AuthError ?? "Sign-in was not completed.";
            }
        }
        catch (Exception ex)
        {
            GitHubAuthMessage = $"Sign-in error: {ex.Message}";
        }
        finally
        {
            IsGitHubBusy = false;
        }
    }

    [RelayCommand]
    public async Task LoginWithTokenAsync()
    {
        if (string.IsNullOrWhiteSpace(ManualToken))
        {
            GitHubAuthMessage = "Please enter a valid GitHub token.";
            return;
        }

        IsGitHubBusy = true;
        GitHubAuthMessage = "Validating token with GitHub...";
        try
        {
            var success = await _gitHubAuthService.LoginWithTokenAsync(ManualToken.Trim());
            if (success)
            {
                GitHubAuthMessage = "Token accepted. Signed in!";
                ManualToken = string.Empty;
            }
            else
            {
                GitHubAuthMessage = _gitHubAuthService.AuthError ?? "Invalid token.";
            }
        }
        catch (Exception ex)
        {
            GitHubAuthMessage = $"Authentication error: {ex.Message}";
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
        GitHubAuthMessage = "Signed out from GitHub.";
    }

    [RelayCommand]
    public void OpenGitHubProfile()
    {
        var url = CurrentGitHubUser?.HtmlUrl;
        if (!string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true });
        }
    }
}
