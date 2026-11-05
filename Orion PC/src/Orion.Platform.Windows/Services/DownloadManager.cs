using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Orion.Core.Models;
using Orion.Core.Services;

namespace Orion.Platform.Windows.Services;

public enum DownloadStatus
{
    Queued,
    Downloading,
    Paused,
    Completed,
    Failed,
    Cancelled
}

public record AuthenticodeInfo(
    bool IsSigned,
    bool IsValid,
    string? Subject,
    string? Issuer,
    string StatusMessage
);

public record DownloadItem(
    string Id,
    string AppId,
    string AppName,
    string Version,
    string Url,
    string LocalFilePath,
    PackageType PackageType,
    long? ExpectedSize,
    string? ExpectedSha256,
    DownloadStatus Status,
    long BytesDownloaded,
    long TotalBytes,
    double ProgressPercent,
    double SpeedBytesPerSec,
    string? ErrorMessage,
    AuthenticodeInfo? SignatureInfo = null,
    string? ComputedSha256 = null,
    bool? HashMatches = null
);

public interface IDownloadManager
{
    IReadOnlyList<DownloadItem> GetQueue();
    DownloadItem Enqueue(AppItem app, AppVariant variant, string? expectedSha256 = null);
    Task StartAsync(string downloadId, CancellationToken cancellationToken = default);
    void Pause(string downloadId);
    void Resume(string downloadId);
    void Cancel(string downloadId);
    AuthenticodeInfo CheckAuthenticode(string filePath);
    event Action<DownloadItem>? DownloadProgressChanged;
    event Action<DownloadItem>? DownloadCompleted;
}

public class DownloadManager : IDownloadManager
{
    private readonly HttpClient _httpClient;
    private readonly IHashVerifier _hashVerifier;
    private readonly IAssetResolver _assetResolver;
    private readonly string _downloadDirectory;
    private readonly string _queueFilePath;
    private readonly ConcurrentDictionary<string, DownloadItem> _items = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cts = new();

    public event Action<DownloadItem>? DownloadProgressChanged;
    public event Action<DownloadItem>? DownloadCompleted;

    public DownloadManager(HttpClient httpClient, IHashVerifier hashVerifier, IAssetResolver assetResolver)
    {
        _httpClient = httpClient;
        _hashVerifier = hashVerifier;
        _assetResolver = assetResolver;

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var baseDir = Path.Combine(localAppData, "OrionStore");
        _downloadDirectory = Path.Combine(baseDir, "Downloads");
        _queueFilePath = Path.Combine(baseDir, "downloads_queue.json");

        Directory.CreateDirectory(_downloadDirectory);
        LoadQueue();
    }

    private void LoadQueue()
    {
        try
        {
            if (!File.Exists(_queueFilePath)) return;
            var json = File.ReadAllText(_queueFilePath);
            var list = JsonSerializer.Deserialize<List<DownloadItem>>(json);
            if (list == null) return;

            foreach (var item in list)
            {
                var status = item.Status == DownloadStatus.Downloading ? DownloadStatus.Paused : item.Status;
                _items[item.Id] = item with { Status = status };
            }
        }
        catch
        {
            // Ignore queue load error
        }
    }

    private void SaveQueue()
    {
        try
        {
            var list = _items.Values.ToList();
            File.WriteAllText(_queueFilePath, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Ignore queue write error
        }
    }

    public IReadOnlyList<DownloadItem> GetQueue() => _items.Values.ToList();

    public DownloadItem Enqueue(AppItem app, AppVariant variant, string? expectedSha256 = null)
    {
        if (!variant.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Insecure download blocked: Orion Store strictly enforces HTTPS for all downloads.");
        }

        var pkgType = variant.PackageType != PackageType.Unknown
            ? variant.PackageType
            : _assetResolver.DeterminePackageType(variant.Url);

        if (!_assetResolver.IsWindowsPackage(pkgType))
        {
            throw new InvalidOperationException($"Refused non-Windows asset: '{variant.Url}' ({pkgType}) cannot be downloaded.");
        }

        var downloadId = $"{app.Id}_{variant.Arch}_{DateTime.UtcNow.Ticks}";
        var fileName = Path.GetFileName(new Uri(variant.Url).AbsolutePath);
        if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            fileName = $"{app.Id}_{variant.Arch}.{(pkgType == PackageType.Msi ? "msi" : pkgType == PackageType.Msix ? "msix" : pkgType == PackageType.ZipPortable ? "zip" : "exe")}";
        }

        var localPath = Path.Combine(_downloadDirectory, fileName);

        var item = new DownloadItem(
            downloadId,
            app.Id,
            app.Name,
            app.Version,
            variant.Url,
            localPath,
            pkgType,
            variant.Size,
            expectedSha256,
            DownloadStatus.Queued,
            0,
            variant.Size ?? 0,
            0,
            0,
            null
        );

        _items[downloadId] = item;
        SaveQueue();
        return item;
    }

    public async Task StartAsync(string downloadId, CancellationToken cancellationToken = default)
    {
        if (!_items.TryGetValue(downloadId, out var item)) return;

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cts[downloadId] = cts;

        try
        {
            item = item with { Status = DownloadStatus.Downloading, ErrorMessage = null };
            _items[downloadId] = item;
            DownloadProgressChanged?.Invoke(item);

            long existingBytes = 0;
            if (File.Exists(item.LocalFilePath))
            {
                existingBytes = new FileInfo(item.LocalFilePath).Length;
            }

            var request = new HttpRequestMessage(HttpMethod.Get, item.Url);
            if (existingBytes > 0)
            {
                request.Headers.Range = new RangeHeaderValue(existingBytes, null);
            }

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            
            bool isPartial = response.StatusCode == System.Net.HttpStatusCode.PartialContent;
            if (!isPartial && existingBytes > 0)
            {
                existingBytes = 0;
            }

            long totalBytes = (response.Content.Headers.ContentLength ?? 0) + existingBytes;
            item = item with { TotalBytes = totalBytes > 0 ? totalBytes : item.TotalBytes };

            await using var contentStream = await response.Content.ReadAsStreamAsync(cts.Token);
            await using var fileStream = new FileStream(item.LocalFilePath, existingBytes > 0 && isPartial ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[81920];
            int bytesRead;
            long totalRead = existingBytes;
            long speedSampleBytes = 0;
            var speedStopwatch = Stopwatch.StartNew();

            while ((bytesRead = await contentStream.ReadAsync(buffer, cts.Token)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cts.Token);
                totalRead += bytesRead;
                speedSampleBytes += bytesRead;

                if (speedStopwatch.ElapsedMilliseconds >= 500)
                {
                    double speed = speedSampleBytes / (speedStopwatch.ElapsedMilliseconds / 1000.0);
                    double percent = totalBytes > 0 ? (double)totalRead / totalBytes * 100.0 : 0;

                    item = item with
                    {
                        BytesDownloaded = totalRead,
                        TotalBytes = totalBytes,
                        ProgressPercent = Math.Clamp(percent, 0, 100),
                        SpeedBytesPerSec = speed
                    };

                    _items[downloadId] = item;
                    DownloadProgressChanged?.Invoke(item);

                    speedSampleBytes = 0;
                    speedStopwatch.Restart();
                }
            }

            fileStream.Flush();

            string? computedHash = null;
            bool? hashMatches = null;
            if (!string.IsNullOrEmpty(item.ExpectedSha256))
            {
                await using var checkStream = File.OpenRead(item.LocalFilePath);
                computedHash = await _hashVerifier.ComputeSha256Async(checkStream, cts.Token);
                hashMatches = string.Equals(computedHash, item.ExpectedSha256.Trim(), StringComparison.OrdinalIgnoreCase);

                if (hashMatches == false)
                {
                    throw new InvalidOperationException($"SHA-256 hash mismatch! Computed: {computedHash}, Expected: {item.ExpectedSha256}");
                }
            }

            var signatureInfo = CheckAuthenticode(item.LocalFilePath);

            item = item with
            {
                Status = DownloadStatus.Completed,
                BytesDownloaded = totalRead,
                ProgressPercent = 100,
                SpeedBytesPerSec = 0,
                ComputedSha256 = computedHash,
                HashMatches = hashMatches,
                SignatureInfo = signatureInfo
            };

            _items[downloadId] = item;
            SaveQueue();
            DownloadProgressChanged?.Invoke(item);
            DownloadCompleted?.Invoke(item);
        }
        catch (OperationCanceledException)
        {
            if (_items.TryGetValue(downloadId, out var cur))
            {
                item = cur with { Status = cur.Status == DownloadStatus.Paused ? DownloadStatus.Paused : DownloadStatus.Cancelled, SpeedBytesPerSec = 0 };
                _items[downloadId] = item;
                SaveQueue();
                DownloadProgressChanged?.Invoke(item);
            }
        }
        catch (Exception ex)
        {
            if (_items.TryGetValue(downloadId, out var cur))
            {
                item = cur with { Status = DownloadStatus.Failed, ErrorMessage = ex.Message, SpeedBytesPerSec = 0 };
                _items[downloadId] = item;
                SaveQueue();
                DownloadProgressChanged?.Invoke(item);
            }
        }
        finally
        {
            _cts.TryRemove(downloadId, out _);
        }
    }

    public void Pause(string downloadId)
    {
        if (_items.TryGetValue(downloadId, out var item) && item.Status == DownloadStatus.Downloading)
        {
            _items[downloadId] = item with { Status = DownloadStatus.Paused, SpeedBytesPerSec = 0 };
            if (_cts.TryGetValue(downloadId, out var cts))
            {
                cts.Cancel();
            }
            SaveQueue();
            DownloadProgressChanged?.Invoke(_items[downloadId]);
        }
    }

    public void Resume(string downloadId)
    {
        if (_items.TryGetValue(downloadId, out var item) && (item.Status == DownloadStatus.Paused || item.Status == DownloadStatus.Failed))
        {
            _ = StartAsync(downloadId);
        }
    }

    public void Cancel(string downloadId)
    {
        if (_items.TryGetValue(downloadId, out var item))
        {
            _items[downloadId] = item with { Status = DownloadStatus.Cancelled, SpeedBytesPerSec = 0 };
            if (_cts.TryGetValue(downloadId, out var cts))
            {
                cts.Cancel();
            }

            if (File.Exists(item.LocalFilePath))
            {
                try { File.Delete(item.LocalFilePath); } catch { /* Ignore */ }
            }

            SaveQueue();
            DownloadProgressChanged?.Invoke(_items[downloadId]);
        }
    }

    public AuthenticodeInfo CheckAuthenticode(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new AuthenticodeInfo(false, false, null, null, "File not found.");
        }

        try
        {
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath));
            bool isValid = cert.Verify();
            string status = isValid
                ? "Valid trusted digital signature."
                : "Digital signature is self-signed or untrusted.";

            return new AuthenticodeInfo(
                IsSigned: true,
                IsValid: isValid,
                Subject: cert.Subject,
                Issuer: cert.Issuer,
                StatusMessage: status
            );
        }
        catch (CryptographicException)
        {
            return new AuthenticodeInfo(
                IsSigned: false,
                IsValid: false,
                Subject: null,
                Issuer: null,
                StatusMessage: "File is unsigned (no embedded digital signature found)."
            );
        }
        catch (Exception ex)
        {
            return new AuthenticodeInfo(
                IsSigned: false,
                IsValid: false,
                Subject: null,
                Issuer: null,
                StatusMessage: $"Signature verification failed: {ex.Message}"
            );
        }
    }
}
