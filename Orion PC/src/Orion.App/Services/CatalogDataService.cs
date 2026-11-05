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
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, MaxDepth = 32 };
    private static readonly JsonDocumentOptions DocOptions = new() { MaxDepth = 32, AllowTrailingCommas = true };
    private const int MaxCatalogSizeBytes = 50 * 1024 * 1024; // 50 MB
    private const int MaxMirrorSizeBytes = 100 * 1024 * 1024; // 100 MB
    private readonly HttpClient _httpClient;
    private readonly ICatalogProcessor _catalogProcessor;
    private readonly IAppLogger _logger;
    private readonly string _cacheDirectory;
    private readonly string _catalogCachePath;
    private readonly string _trimmedMirrorCachePath;
    private List<AppItem> _allApps = new();
    private StoreConfig? _currentConfig;

    public StoreConfig? CurrentConfig => _currentConfig;
    public IReadOnlyList<AppItem> AllApps => _allApps;

    public CatalogDataService(HttpClient httpClient, ICatalogProcessor catalogProcessor, IAppLogger logger)
    {
        _httpClient = httpClient;
        _catalogProcessor = catalogProcessor;
        _logger = logger;
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _cacheDirectory = Path.Combine(localAppData, "OrionStore", "Cache");
        _catalogCachePath = Path.Combine(_cacheDirectory, "cached_catalog.json");
        _trimmedMirrorCachePath = Path.Combine(_cacheDirectory, "trimmed_mirror.json");
        Directory.CreateDirectory(_cacheDirectory);
    }

    public async Task<StoreConfig?> LoadConfigAsync(CancellationToken cancellationToken = default)
    {
        if (_currentConfig != null) return _currentConfig;

        foreach (var url in Defaults.ConfigUrls)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(5));

                var json = await _httpClient.GetStringAsync(url, cts.Token).ConfigureAwait(false);
                _currentConfig = JsonSerializer.Deserialize<StoreConfig>(json, JsonOptions);
                if (_currentConfig != null) return _currentConfig;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Config mirror failed ({url}): {ex.Message}");
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

        return await Task.Run(async () =>
        {
            var config = await LoadConfigAsync(cancellationToken).ConfigureAwait(false);
            var appsUrl = config != null && !string.IsNullOrEmpty(config.AppsJsonUrl)
                ? config.AppsJsonUrl
                : Defaults.AppsUrls[0];

            // 1. Try remote fetch
            try
            {
                using var appsCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                appsCts.CancelAfter(TimeSpan.FromSeconds(10));

                var appsJson = await _httpClient.GetStringAsync(appsUrl, appsCts.Token).ConfigureAwait(false);
                if (appsJson.Length > MaxCatalogSizeBytes)
                {
                    throw new InvalidOperationException($"Remote catalog exceeds maximum allowed size ({appsJson.Length} bytes).");
                }
                using var doc = JsonDocument.Parse(appsJson, DocOptions);

                // Fetch or load mirror
                var mirror = await LoadOrFetchMirrorAsync(config?.MirrorJsonUrl ?? Defaults.MirrorJson, cancellationToken).ConfigureAwait(false);

                var processed = _catalogProcessor.ProcessCatalog(doc.RootElement, mirror);
                if (processed.Count > 0)
                {
                    _allApps = processed.ToList();
                    try
                    {
                        await WriteCacheWithIntegrityAsync(_catalogCachePath, JsonSerializer.Serialize(_allApps, JsonOptions), cancellationToken).ConfigureAwait(false);
                    }
                    catch { /* non-critical */ }

                    return (IReadOnlyList<AppItem>)_allApps;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Remote catalog fetch failed: {ex.Message}. Falling back to disk cache.");
            }

            // 2. Try disk cache with cryptographic integrity verification
            var cachedJson = await ReadCacheWithIntegrityAsync(_catalogCachePath, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(cachedJson))
            {
                try
                {
                    var cached = JsonSerializer.Deserialize<List<AppItem>>(cachedJson, JsonOptions);
                    if (cached != null && cached.Count > 0)
                    {
                        _allApps = cached;
                        _logger.LogInfo($"Loaded {_allApps.Count} apps from disk cache.");
                        return _allApps;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to deserialize disk cache: {ex.Message}");
                }
            }

            // 3. Fall back to embedded local_apps.json
            try
            {
                var localPath = Path.Combine(AppContext.BaseDirectory, "Assets", "local_apps.json");
                if (File.Exists(localPath))
                {
                    var localJson = await File.ReadAllTextAsync(localPath, cancellationToken).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(localJson, DocOptions);
                    var emptyMirror = new Dictionary<string, IReadOnlyList<ReleaseInfo>>();
                    _allApps = _catalogProcessor.ProcessCatalog(doc.RootElement, emptyMirror).ToList();
                    _logger.LogInfo($"Loaded {_allApps.Count} fallback apps from local_apps.json.");
                    return _allApps;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to load local_apps.json fallback", ex);
            }

            return _allApps;
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<ReleaseInfo>>> LoadOrFetchMirrorAsync(string mirrorUrl, CancellationToken cancellationToken)
    {
        // Check disk cache first (< 24h old) with integrity verification
        if (File.Exists(_trimmedMirrorCachePath))
        {
            try
            {
                var fileInfo = new FileInfo(_trimmedMirrorCachePath);
                if (DateTime.UtcNow - fileInfo.LastWriteTimeUtc < TimeSpan.FromHours(24))
                {
                    var cachedJson = await ReadCacheWithIntegrityAsync(_trimmedMirrorCachePath, cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(cachedJson))
                    {
                        var cachedMirror = JsonSerializer.Deserialize<Dictionary<string, List<ReleaseInfo>>>(cachedJson, JsonOptions);
                        if (cachedMirror != null && cachedMirror.Count > 0)
                        {
                            return cachedMirror.ToDictionary(k => k.Key, v => (IReadOnlyList<ReleaseInfo>)v.Value);
                        }
                    }
                }
            }
            catch { /* proceed to network fetch */ }
        }

        var mirror = new Dictionary<string, IReadOnlyList<ReleaseInfo>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var mirrorCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            mirrorCts.CancelAfter(TimeSpan.FromSeconds(12)); // bounded timeout for mirror

            using var mirrorStream = await _httpClient.GetStreamAsync(mirrorUrl, mirrorCts.Token).ConfigureAwait(false);
            var decoded = _catalogProcessor.DecodeMirror(mirrorStream);
            foreach (var (k, v) in decoded)
            {
                mirror[k] = v;
            }

            // Cache trimmed mirror to disk for instant startup next time with integrity hash
            try
            {
                await WriteCacheWithIntegrityAsync(_trimmedMirrorCachePath, JsonSerializer.Serialize(mirror, JsonOptions), cancellationToken).ConfigureAwait(false);
            }
            catch { /* non-critical */ }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Mirror download failed or timed out ({ex.Message}). Continuing with available data.");
        }

        return mirror;
    }

    private static async Task WriteCacheWithIntegrityAsync(string filePath, string content, CancellationToken ct)
    {
        await File.WriteAllTextAsync(filePath, content, ct).ConfigureAwait(false);
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        var hashHex = Convert.ToHexString(hash);
        await File.WriteAllTextAsync(filePath + ".sha256", hashHex, ct).ConfigureAwait(false);
    }

    private async Task<string?> ReadCacheWithIntegrityAsync(string filePath, CancellationToken ct)
    {
        if (!File.Exists(filePath)) return null;
        var shaPath = filePath + ".sha256";
        if (!File.Exists(shaPath))
        {
            _logger.LogWarning($"Cache file {filePath} missing integrity checksum. Discarding.");
            return null;
        }

        var content = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
        var expectedHash = (await File.ReadAllTextAsync(shaPath, ct).ConfigureAwait(false)).Trim();

        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var actualHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));

        if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning($"Cache integrity check failed for {filePath}! Computed: {actualHash}, Expected: {expectedHash}");
            return null;
        }

        return content;
    }

    public IReadOnlyList<AppItem> FilterAppsByPlatform(PlatformCategory category)
    {
        return _catalogProcessor.FilterByPlatform(_allApps, category);
    }
}
