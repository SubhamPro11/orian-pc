using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orion.App.Services;
using Orion.Core.Models;
using Orion.Core.Services;
using Orion.Platform.Windows.Services;

namespace Orion.App.ViewModels;

public record MyAppItemModel(
    AppItem? CatalogApp,
    InstalledAppInfo InstalledInfo,
    bool HasUpdate,
    string? NewVersion
);

public partial class MyAppsViewModel : ObservableObject
{
    private readonly IInstalledAppDetector _appTracker;
    private readonly ICatalogDataService _catalogService;
    private readonly IVersionService _versionService;
    private readonly IWindowsInstallerService _installerService;
    private readonly MainViewModel _mainViewModel;

    [ObservableProperty]
    private ObservableCollection<MyAppItemModel> _installedApps = new();

    [ObservableProperty]
    private int _installedCount = 0;

    [ObservableProperty]
    private int _updatesCount = 0;

    [ObservableProperty]
    private bool _isLoading = false;

    public MyAppsViewModel(
        IInstalledAppDetector appTracker,
        ICatalogDataService catalogService,
        IVersionService versionService,
        IWindowsInstallerService installerService,
        MainViewModel mainViewModel)
    {
        _appTracker = appTracker;
        _catalogService = catalogService;
        _versionService = versionService;
        _installerService = installerService;
        _mainViewModel = mainViewModel;
    }

    public async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            var apps = await _catalogService.LoadAppsAsync();
            var installedList = _appTracker.GetInstalledApps(forceRefresh: true);

            var models = new List<MyAppItemModel>();
            int updates = 0;

            foreach (var installed in installedList)
            {
                var match = apps.FirstOrDefault(a =>
                    a.Name.Equals(installed.DisplayName, StringComparison.OrdinalIgnoreCase) ||
                    (installed.DisplayName.Contains(a.Name, StringComparison.OrdinalIgnoreCase) && a.IsWindowsApp));

                bool hasUpdate = false;
                string? newVer = null;

                if (match != null && !string.IsNullOrEmpty(installed.DisplayVersion))
                {
                    hasUpdate = _versionService.HasAvailableUpdate(match, installed.DisplayVersion);
                    if (hasUpdate)
                    {
                        updates++;
                        newVer = match.Version;
                    }
                }

                models.Add(new MyAppItemModel(match, installed, hasUpdate, newVer));
            }

            InstalledApps.Clear();
            foreach (var m in models.OrderByDescending(x => x.HasUpdate).ThenBy(x => x.InstalledInfo.DisplayName))
            {
                InstalledApps.Add(m);
            }

            InstalledCount = models.Count;
            UpdatesCount = updates;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void OpenApp(MyAppItemModel model)
    {
        if (model.CatalogApp != null)
        {
            _mainViewModel.SelectApp(model.CatalogApp);
        }
    }

    [RelayCommand]
    public async Task UninstallAsync(MyAppItemModel model)
    {
        await _installerService.UninstallAsync(model.InstalledInfo);
        await RefreshAsync();
    }
}

