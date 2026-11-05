using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orion.Platform.Windows.Services;

namespace Orion.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISecureStorageService _secureStorage;

    [ObservableProperty]
    private string _virusTotalApiKey = string.Empty;

    [ObservableProperty]
    private string _selectedTheme = "Default";

    [ObservableProperty]
    private bool _useMicaBackdrop = true;

    [ObservableProperty]
    private string _saveMessage = string.Empty;

    public SettingsViewModel(ISecureStorageService secureStorage)
    {
        _secureStorage = secureStorage;
        _virusTotalApiKey = _secureStorage.GetSecret("VirusTotalApiKey") ?? string.Empty;
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
}

