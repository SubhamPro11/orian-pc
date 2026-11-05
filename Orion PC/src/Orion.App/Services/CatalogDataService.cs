using System.Text.Json;
using Orion.Core.Models;
using Orion.Core.Services;

namespace Orion.App.Services;

public interface ICatalogDataService
{
    Task<IReadOnlyList<AppItem>> LoadAppsAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
    Task<StoreConfig?> LoadConfigAsync(CancellationToken cancellationToken = default);
    StoreConfig? CurrentConfig { get; }
    IReadOnlyList<AppItem> AllApps { get; }
    IReadOnlyList<AppItem> FilterAppsByPlatform(PlatformCategory category);
}

public class CatalogDataService : ICatalogDataService
{
    private readonly HttpClient _httpClient;
    private readonly ICatalogProcessor _catalogProcessor;
    private readonly string _cacheDirectory;
    private readonly string _catalogCachePath;
    private List<AppItem> _allApps = new();
    private StoreConfig? _currentConfig;

    public StoreConfig? CurrentConfig => _currentConfig;
    public IReadOnlyList<AppItem> AllApps => _allApps;

    public CatalogDataService(HttpClient httpClient, ICatalogProcessor catalogProcessor)
    {
        _httpClient = httpClient;
        _catalogProcessor = catalogProcessor;
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _cacheDirectory = Path.Combine(localAppData, "OrionStore", "Cache");
        _catalogCachePath = Path.Combine(_cacheDirectory, "cached_catalog.json");
        Directory.CreateDirectory(_cacheDirectory);
    }

    public async Task<StoreConfig?> LoadConfigAsync(CancellationToken cancellationToken = default)
    {
        if (_currentConfig != null) return _currentConfig;

        foreach (var url in Defaults.ConfigUrls)
        {
            try
            {
                var json = await _httpClient.GetStringAsync(url, cancellationToken);
                _currentConfig = JsonSerializer.Deserialize<StoreConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (_currentConfig != null) return _currentConfig;
            }
            catch
            {
                // Try next mirror
            }
        }

        return null;
    }

    public async Task<IReadOnlyList<AppItem>> LoadAppsAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (_allApps.Count > 0 && !forceRefresh)
        {
            return _allApps;
        }

        var config = await LoadConfigAsync(cancellationToken);
        var appsUrl = config != null && !string.IsNullOrEmpty(config.AppsJsonUrl)
            ? config.AppsJsonUrl
            : Defaults.AppsUrls[0];

        // 1. Try remote fetch
        try
        {
            var appsJson = await _httpClient.GetStringAsync(appsUrl, cancellationToken);
            using var doc = JsonDocument.Parse(appsJson);

            // Fetch mirror if available (or use empty mirror dictionary)
            var mirror = new Dictionary<string, IReadOnlyList<ReleaseInfo>>(StringComparer.OrdinalIgnoreCase);
            var mirrorUrl = config?.MirrorJsonUrl ?? Defaults.MirrorJson;
            try
            {
                using var mirrorStream = await _httpClient.GetStreamAsync(mirrorUrl, cancellationToken);
                var decoded = _catalogProcessor.DecodeMirror(mirrorStream);
                foreach (var (k, v) in decoded) mirror[k] = v;
            }
            catch
            {
                // Proceed with catalog even if mirror failed
            }

            var processed = _catalogProcessor.ProcessCatalog(doc.RootElement, mirror);
            if (processed.Count > 0)
            {
                _allApps = processed.ToList();
                // Cache to disk
                try
                {
                    await File.WriteAllTextAsync(_catalogCachePath, JsonSerializer.Serialize(_allApps), cancellationToken);
                }
                catch { /* non-critical */ }

                return _allApps;
            }
        }
        catch
        {
            // Network failed, fall back to disk or embedded
        }

        // 2. Try disk cache
        if (File.Exists(_catalogCachePath))
        {
            try
            {
                var cachedJson = await File.ReadAllTextAsync(_catalogCachePath, cancellationToken);
                var cached = JsonSerializer.Deserialize<List<AppItem>>(cachedJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (cached != null && cached.Count > 0)
                {
                    _allApps = cached;
                    return _allApps;
                }
            }
            catch { /* proceed to embedded */ }
        }

        // 3. Fall back to embedded local_apps.json
        try
        {
            var localPath = Path.Combine(AppContext.BaseDirectory, "Assets", "local_apps.json");
            if (File.Exists(localPath))
            {
                var localJson = await File.ReadAllTextAsync(localPath, cancellationToken);
                using var doc = JsonDocument.Parse(localJson);
                var emptyMirror = new Dictionary<string, IReadOnlyList<ReleaseInfo>>();
                _allApps = _catalogProcessor.ProcessCatalog(doc.RootElement, emptyMirror).ToList();
                return _allApps;
            }
        }
        catch { /* ignore */ }

        return _allApps;
    }

    public IReadOnlyList<AppItem> FilterAppsByPlatform(PlatformCategory category)
    {
        return _catalogProcessor.FilterByPlatform(_allApps, category);
    }
}

