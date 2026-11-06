using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orion.App.Services;
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
    private readonly ICatalogDataService _catalogDataService;
    private readonly ICatalogProcessor _catalogProcessor;
    private readonly IAppLogger _logger;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue;

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
        MainViewModel mainViewModel,
        ICatalogDataService catalogDataService,
        ICatalogProcessor catalogProcessor,
        IAppLogger logger)
    {
        _assetResolver = assetResolver;
        _appTracker = appTracker;
        _downloadManager = downloadManager;
        _installerService = installerService;
        _virusTotalService = virusTotalService;
        _mainViewModel = mainViewModel;
        _catalogDataService = catalogDataService;
        _catalogProcessor = catalogProcessor;
        _logger = logger;
        _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
    }

    public void SetApp(AppItem app)
    {
        App = app;
        IsWindowsApp = app.IsWindowsApp;

        // Check installed status safely
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

        UpdateVariants(app, osArch);

        if (IsWindowsApp)
        {
            CanInstall = SelectedVariant != null;
            NonWindowsNotice = string.Empty;

            if (IsInstalled)
            {
                PrimaryActionText = "Installed";
                StatusMessage = $"Installed: v{InstalledVersion}";
            }
            else
            {
                PrimaryActionText = "Download & Install";
                StatusMessage = SelectedVariant != null ? "Ready to download and install." : "No download asset available.";
            }

            RecommendedArchBadge = $"Recommended for your machine ({osArch})";

            // If this PC app has a GitHub repo and either has no variants or download url is #, fetch releases from GitHub API
            if ((app.VariantsList.Count == 0 || app.DownloadUrl == "#" || app.DownloadUrl.StartsWith("http://#", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(app.DownloadUrl)) && !string.IsNullOrWhiteSpace(app.GithubRepo))
            {
                _ = FetchLiveReleasesAsync(app, osArch);
            }
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

    private void UpdateVariants(AppItem app, CpuArchitecture osArch)
    {
        AvailableVariants.Clear();
        foreach (var v in app.VariantsList)
        {
            AvailableVariants.Add(v);
        }

        // Recommend matching variant
        SelectedVariant = _assetResolver.SelectBestAsset(app.VariantsList, osArch);
    }

    private async Task FetchLiveReleasesAsync(AppItem app, CpuArchitecture osArch)
    {
        try
        {
            StatusMessage = "Checking GitHub for latest Windows releases...";
            var releases = await _catalogDataService.FetchGitHubReleasesAsync(app.GithubRepo!).ConfigureAwait(false);
            if (releases != null && releases.Count > 0)
            {
                var updated = _catalogProcessor.ApplyReleases(app, releases);
                RunOnUIThread(() =>
                {
                    App = updated;
                    UpdateVariants(updated, osArch);
                    if (SelectedVariant != null)
                    {
                        CanInstall = true;
                        PrimaryActionText = IsInstalled ? "Installed" : "Download & Install";
                        StatusMessage = $"Ready to install {updated.Version}";
                    }
                    else
                    {
                        StatusMessage = "No Windows installer package (.exe/.msi/.zip) found in GitHub releases.";
                    }
                });
            }
            else
            {
                RunOnUIThread(() =>
                {
                    if (SelectedVariant == null)
                    {
                        StatusMessage = "No downloadable release assets found on GitHub.";
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning($"Error loading releases for {app.Name}: {ex.Message}");
        }
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

    public Func<string, string, Task<bool>>? ConfirmActionAsync { get; set; }

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

        DownloadItem? downloadItem = null;
        Action<DownloadItem>? onProgress = null;
        Action<DownloadItem>? onCompleted = null;

        try
        {
            // Enforce service-level refusal
            _installerService.ValidateAssetForWindows(App, SelectedVariant);

            downloadItem = _downloadManager.Enqueue(App, SelectedVariant, SelectedVariant.Sha256 ?? App.Sha256);
            StatusMessage = $"Downloading {App.Name}...";

            var tcs = new TaskCompletionSource<DownloadItem>(TaskCreationOptions.RunContinuationsAsynchronously);

            onCompleted = item =>
            {
                if (item.Id == downloadItem.Id)
                {
                    tcs.TrySetResult(item);
                }
            };

            onProgress = item =>
            {
                if (item.Id == downloadItem.Id)
                {
                    if (item.Status == DownloadStatus.Failed)
                    {
                        tcs.TrySetException(new InvalidOperationException(item.ErrorMessage ?? "Download failed."));
                    }
                    else if (item.Status == DownloadStatus.Cancelled)
                    {
                        tcs.TrySetCanceled();
                    }
                }
            };

            _downloadManager.DownloadCompleted += onCompleted;
            _downloadManager.DownloadProgressChanged += onProgress;

            await _downloadManager.StartAsync(downloadItem.Id);
            var completedItem = await tcs.Task;

            // Pre-install Authenticode inspection
            SignatureInfo = _downloadManager.CheckAuthenticode(completedItem.LocalFilePath);
            StatusMessage = $"Verifying: {SignatureInfo.StatusMessage}";

            // Security gate: require user confirmation for unverified hashes or unsigned binaries
            bool isHashVerified = completedItem.HashMatches == true;
            bool isSignatureValid = SignatureInfo.IsValid;

            if (!isHashVerified || !isSignatureValid)
            {
                string reason = (!isHashVerified && !isSignatureValid)
                    ? "This package has not been verified by a catalog checksum and does not have a valid digital signature."
                    : !isSignatureValid
                        ? "This installer does not have a verified digital signature."
                        : "This installer does not match a verified catalog checksum.";

                string warningMessage = $"{reason}\n\n" +
                    $"File: {Path.GetFileName(completedItem.LocalFilePath)}\n" +
                    $"SHA-256: {completedItem.ComputedSha256 ?? "Unknown"}\n" +
                    $"Signature: {SignatureInfo.StatusMessage}\n\n" +
                    "Running unverified software may pose security risks. Do you want to proceed with the installation?";

                bool userConfirmed = false;
                if (ConfirmActionAsync != null)
                {
                    userConfirmed = await ConfirmActionAsync("Security Warning: Unverified Application", warningMessage);
                }

                if (!userConfirmed)
                {
                    StatusMessage = "Installation cancelled: package unverified.";
                    return;
                }
            }

            // Run installer
            StatusMessage = "Running installer...";
            var result = await _installerService.InstallAsync(App, SelectedVariant, completedItem.LocalFilePath);

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
        catch (OperationCanceledException)
        {
            StatusMessage = "Download was cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            if (onCompleted != null) _downloadManager.DownloadCompleted -= onCompleted;
            if (onProgress != null) _downloadManager.DownloadProgressChanged -= onProgress;
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void OpenProjectSource()
    {
        string? url = null;
        if (!string.IsNullOrWhiteSpace(App?.RepoUrl))
        {
            url = App.RepoUrl.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(App?.GithubRepo))
        {
            var gh = App.GithubRepo.Trim();
            url = gh.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || gh.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? gh
                : $"https://github.com/{gh.TrimStart('/')}";
        }
        else if (!string.IsNullOrWhiteSpace(App?.OfficialSite))
        {
            url = App.OfficialSite.Trim();
        }

        SafeOpenUrl(url);
    }

    [RelayCommand]
    public void OpenOfficialSite()
    {
        var url = !string.IsNullOrWhiteSpace(App?.OfficialSite) && App.OfficialSite != "#"
            ? App.OfficialSite
            : App?.DownloadUrl;
        SafeOpenUrl(url);
    }

    [RelayCommand]
    public void CopyLink()
    {
        if (App == null) return;
        var url = App.OfficialSite ?? App.RepoUrl ?? App.DownloadUrl;
        if (!string.IsNullOrWhiteSpace(url) && url != "#" &&
            Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            var dataPackage = new global::Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(uri.AbsoluteUri);
            global::Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
            StatusMessage = "Link copied to clipboard!";
        }
    }

    private static void SafeOpenUrl(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl) || rawUrl == "#") return;

        if (Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true });
        }
    }
}
