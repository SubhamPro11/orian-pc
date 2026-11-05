using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orion.App.Services;
using Orion.Core.Models;
using Orion.Core.Services;

namespace Orion.App.ViewModels;

public partial class SearchViewModel : ObservableObject, IDisposable
{
    private readonly ICatalogDataService _catalogService;
    private readonly ICatalogProcessor _catalogProcessor;
    private readonly MainViewModel _mainViewModel;
    private readonly Action<PlatformCategory> _deviceChangedHandler;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private string _selectedCategory = "All";

    [ObservableProperty]
    private SortOption _selectedSort = SortOption.Newest;

    [ObservableProperty]
    private ObservableCollection<AppItem> _results = new();

    [ObservableProperty]
    private ObservableCollection<string> _categories = new();

    [ObservableProperty]
    private bool _isLoading = false;

    public SearchViewModel(ICatalogDataService catalogService, ICatalogProcessor catalogProcessor, MainViewModel mainViewModel)
    {
        _catalogService = catalogService;
        _catalogProcessor = catalogProcessor;
        _mainViewModel = mainViewModel;

        _deviceChangedHandler = _ => PerformSearch();
        _mainViewModel.DeviceCategoryChanged += _deviceChangedHandler;
    }

    public void Initialize()
    {
        Categories.Clear();
        Categories.Add("All");
        Categories.Add("Utility");
        Categories.Add("Productivity");
        Categories.Add("Media");
        Categories.Add("Music");
        Categories.Add("Social");
        Categories.Add("Development");
        Categories.Add("Privacy");
        Categories.Add("Reading");
        Categories.Add("Games");

        PerformSearch();
    }

    partial void OnQueryChanged(string value) => PerformSearch();
    partial void OnSelectedCategoryChanged(string value) => PerformSearch();
    partial void OnSelectedSortChanged(SortOption value) => PerformSearch();

    [RelayCommand]
    public void PerformSearch()
    {
        var platformApps = _catalogService.FilterAppsByPlatform(_mainViewModel.SelectedDevice);
        var searchOutput = _catalogProcessor.Search(Query, SelectedCategory, SelectedSort, platformApps);

        Results.Clear();
        foreach (var app in searchOutput)
        {
            Results.Add(app);
        }
    }

    [RelayCommand]
    public void OpenApp(AppItem app)
    {
        _mainViewModel.SelectApp(app);
    }

    public void Dispose()
    {
        _mainViewModel.DeviceCategoryChanged -= _deviceChangedHandler;
        GC.SuppressFinalize(this);
    }
}

