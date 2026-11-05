using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orion.Core.Models;
using Orion.Core.Services;
using Orion.Platform.Windows.Services;

namespace Orion.App.ViewModels;

public partial class AppDetailViewModel : ObservableObject
{
    private readonly IAssetResolver _assetResolver;
    private readonly IInstalledAppDetector _appTracker;
    private readonly IDownloadManager _downloadManager;
    private readonly IWindowsInstallerService _installerService;
    private readonly IVirusTotalService _virusTotalService;
    private readonly MainViewModel _mainViewModel;

    [ObservableProperty]
    private AppItem? _app;

    [ObservableProperty]
    private bool _isWindowsApp;

    [ObservableProperty]
    private bool _isInstalled;

    [ObservableProperty]
    private string? _installedVersion;

    [ObservableProperty]
    private bool _hasUpdate;

    [ObservableProperty]
    private ObservableCollection<AppVariant> _availableVariants = new();

    [ObservableProperty]
    private AppVariant? _selectedVariant;

    [ObservableProperty]
    private string _recommendedArchBadge = string.Empty;

    [ObservableProperty]
    private string _primaryActionText = "Install";

    [ObservableProperty]
    private bool _canInstall = true;

    [ObservableProperty]
    private string _nonWindowsNotice = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy = false;

    [ObservableProperty]
    private AuthenticodeInfo? _signatureInfo;

    [ObservableProperty]
    private VtScanResult? _vtResult;

    public AppDetailViewModel(
        IAssetResolver assetResolver,
        IInstalledAppDetector appTracker,
        IDownloadManager downloadManager,
        IWindowsInstallerService installerService,
        IVirusTotalService virusTotalService,
        MainViewModel mainViewModel)
    {
        _assetResolver = assetResolver;
        _appTracker = appTracker;
        _downloadManager = downloadManager;
        _installerService = installerService;
        _virusTotalService = virusTotalService;
        _mainViewModel = mainViewModel;
    }

    public void SetApp(AppItem app)
    {
        App = app;
        IsWindowsApp = app.IsWindowsApp;

        // Check installed status
        var installed = _appTracker.FindInstalledApp(app);
        IsInstalled = installed != null;
        InstalledVersion = installed?.DisplayVersion;

        // Architecture detection for user's machine
        var osArch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => CpuArchitecture.X64,
            Architecture.Arm64 => CpuArchitecture.Arm64,
            Architecture.X86 => CpuArchitecture.X86,
            _ => CpuArchitecture.Universal
        };

        AvailableVariants.Clear();
        foreach (var v in app.VariantsList)
        {
            AvailableVariants.Add(v);
        }

        // Recommend matching variant
        SelectedVariant = _assetResolver.SelectBestAsset(app.VariantsList, osArch);

        if (IsWindowsApp)
        {
            CanInstall = true;
            NonWindowsNotice = string.Empty;

            if (IsInstalled)
            {
                PrimaryActionText = "Installed";
                StatusMessage = $"Installed: v{InstalledVersion}";
            }
            else
            {
                PrimaryActionText = "Download & Install";
                StatusMessage = "Ready to download and install.";
            }

            RecommendedArchBadge = $"Recommended for your machine ({osArch})";
        }
        else
        {
            // CORE BEHAVIOR RULE: When user views non-Windows apps, block download & install
            CanInstall = false;
            PrimaryActionText = "Not available on Windows";
            NonWindowsNotice = $"This application is created for {app.Platform}. Orion Store for PC only installs native Windows applications.";
            StatusMessage = "Download blocked: non-Windows app.";
        }
    }

    [RelayCommand]
    public async Task InstallAsync()
    {
        if (App == null || SelectedVariant == null) return;

        // Refuse non-Windows in UI
        if (!IsWindowsApp)
        {
            StatusMessage = "Installation rejected: this application is not for Windows.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Queueing download...";

        try
        {
            // Enforce service-level refusal
            _installerService.ValidateAssetForWindows(App, SelectedVariant);

            var downloadItem = _downloadManager.Enqueue(App, SelectedVariant);
            StatusMessage = $"Downloading {App.Name}...";

            var tcs = new TaskCompletionSource<bool>();
            void OnCompleted(DownloadItem item)
            {
                if (item.Id == downloadItem.Id)
                {
                    _downloadManager.DownloadCompleted -= OnCompleted;
                    tcs.TrySetResult(true);
                }
            }
            _downloadManager.DownloadCompleted += OnCompleted;

            await _downloadManager.StartAsync(downloadItem.Id);
            await tcs.Task;

            // Pre-install Authenticode and VirusTotal inspection
            SignatureInfo = _downloadManager.CheckAuthenticode(downloadItem.LocalFilePath);
            StatusMessage = $"Verifying: {SignatureInfo.StatusMessage}";

            // Run installer
            StatusMessage = "Running installer...";
            var result = await _installerService.InstallAsync(App, SelectedVariant, downloadItem.LocalFilePath);

            if (result.Success)
            {
                IsInstalled = true;
                PrimaryActionText = "Installed";
                StatusMessage = "Installation finished successfully!";
            }
            else
            {
                StatusMessage = $"Install message: {result.Message}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void OpenProjectSource()
    {
        var url = App?.RepoUrl ?? App?.GithubRepo?.Insert(0, "https://github.com/") ?? App?.OfficialSite;
        if (!string.IsNullOrEmpty(url))
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
    }

    [RelayCommand]
    public void OpenOfficialSite()
    {
        var url = App?.OfficialSite ?? App?.DownloadUrl;
        if (!string.IsNullOrEmpty(url) && url != "#")
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
    }

    [RelayCommand]
    public void CopyLink()
    {
        if (App == null) return;
        var url = App.OfficialSite ?? App.RepoUrl ?? App.DownloadUrl;
        if (!string.IsNullOrEmpty(url))
        {
            var dataPackage = new global::Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(url);
            global::Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
            StatusMessage = "Link copied to clipboard!";
        }
    }
}

