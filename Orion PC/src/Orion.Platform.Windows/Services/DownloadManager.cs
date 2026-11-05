using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    private readonly string _tempDirectory;
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
        _tempDirectory = Path.Combine(_downloadDirectory, "Temp");
        _queueFilePath = Path.Combine(baseDir, "downloads_queue.json");

        Directory.CreateDirectory(_downloadDirectory);
        Directory.CreateDirectory(_tempDirectory);
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
                var restored = item.Status == DownloadStatus.Downloading
                    ? item with { Status = DownloadStatus.Paused, SpeedBytesPerSec = 0 }
                    : item;

                _items[restored.Id] = restored;
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
            var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_queueFilePath, json);
        }
        catch
        {
            // Ignore queue save error
        }
    }

    public IReadOnlyList<DownloadItem> GetQueue()
    {
        return _items.Values.OrderByDescending(i => i.Id).ToList();
    }

    private static PackageType DeterminePackageTypeFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var path = uri.AbsolutePath;
            var ext = Path.GetExtension(path).ToLowerInvariant();

            return ext switch
            {
                ".msix" or ".appx" => PackageType.Msix,
                ".msixbundle" or ".appxbundle" => PackageType.Msix,
                ".msi" => PackageType.Msi,
                ".exe" => PackageType.Exe,
                ".zip" => PackageType.ZipPortable,
                _ => PackageType.Unknown
            };
        }
        catch
        {
            return PackageType.Unknown;
        }
    }

    public DownloadItem Enqueue(AppItem app, AppVariant variant, string? expectedSha256 = null)
    {
        if (app == null) throw new ArgumentNullException(nameof(app));
        if (variant == null) throw new ArgumentNullException(nameof(variant));

        // Enforce HTTPS-only
        if (!variant.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Insecure download blocked: '{variant.Url}' must use HTTPS.");
        }

        // Enforce Windows target
        if (!app.IsWindowsApp)
        {
            throw new InvalidOperationException($"Refused non-Windows app: '{app.Name}' is an {app.Platform} application.");
        }

        var pkgType = variant.PackageType != PackageType.Unknown
            ? variant.PackageType
            : DeterminePackageTypeFromUrl(variant.Url);

        if (!_assetResolver.IsWindowsPackage(pkgType))
        {
            throw new InvalidOperationException($"Refused non-Windows package type: '{pkgType}' from '{variant.Url}'.");
        }

        var downloadId = Guid.NewGuid().ToString("N");

        string ext = pkgType switch
        {
            PackageType.Exe => ".exe",
            PackageType.Msi => ".msi",
            PackageType.Msix => ".msix",
            PackageType.ZipPortable => ".zip",
            _ => ".bin"
        };

        var safeFileName = $"{Regex.Replace(app.Name, @"[^a-zA-Z0-9_\-]", "_")}_{downloadId.Substring(0, 8)}{ext}";
        var localFilePath = Path.Combine(_downloadDirectory, safeFileName);

        var item = new DownloadItem(
            downloadId,
            app.Id,
            app.Name,
            app.Version,
            variant.Url,
            localFilePath,
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

    private static void CheckDiskSpace(string targetPath, long requiredBytes)
    {
        if (requiredBytes <= 0) return;
        try
        {
            var driveRoot = Path.GetPathRoot(Path.GetFullPath(targetPath));
            if (!string.IsNullOrEmpty(driveRoot))
            {
                var driveInfo = new DriveInfo(driveRoot);
                if (driveInfo.AvailableFreeSpace < requiredBytes + (50 * 1024 * 1024)) // 50 MB buffer
                {
                    throw new IOException($"Insufficient disk space on drive {driveRoot}. Required: {requiredBytes / 1024 / 1024} MB, Available: {driveInfo.AvailableFreeSpace / 1024 / 1024} MB.");
                }
            }
        }
        catch (IOException)
        {
            throw;
        }
        catch
        {
            // Ignore DriveInfo query issues
        }
    }

    public async Task StartAsync(string downloadId, CancellationToken cancellationToken = default)
    {
        if (!_items.TryGetValue(downloadId, out var item)) return;

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cts[downloadId] = cts;

        var partDir = Path.Combine(_tempDirectory, downloadId);
        Directory.CreateDirectory(partDir);
        var partFile = Path.Combine(partDir, "download.part");

        try
        {
            item = item with { Status = DownloadStatus.Downloading, ErrorMessage = null };
            _items[downloadId] = item;
            DownloadProgressChanged?.Invoke(item);

            long existingBytes = 0;
            if (File.Exists(partFile))
            {
                existingBytes = new FileInfo(partFile).Length;
            }

            var request = new HttpRequestMessage(HttpMethod.Get, item.Url);
            if (existingBytes > 0)
            {
                request.Headers.Range = new RangeHeaderValue(existingBytes, null);
            }

            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            // Handle 416 Range Not Satisfiable: reset and restart
            if (response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                response.Dispose();
                existingBytes = 0;
                try { File.Delete(partFile); } catch { }

                request = new HttpRequestMessage(HttpMethod.Get, item.Url);
                response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            }

            // Ensure response is successful (200 OK or 206 Partial Content)
            response.EnsureSuccessStatusCode();

            // Verify final redirect is HTTPS
            if (response.RequestMessage?.RequestUri != null &&
                !response.RequestMessage.RequestUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Redirected to non-HTTPS URL: {response.RequestMessage.RequestUri}");
            }

            bool isPartial = response.StatusCode == System.Net.HttpStatusCode.PartialContent;
            if (!isPartial && existingBytes > 0)
            {
                existingBytes = 0;
                try { File.Delete(partFile); } catch { }
            }

            long totalBytes = (response.Content.Headers.ContentLength ?? 0) + existingBytes;
            item = item with { TotalBytes = totalBytes > 0 ? totalBytes : item.TotalBytes };

            // Disk space check
            CheckDiskSpace(partFile, totalBytes - existingBytes);

            long totalRead = existingBytes;

            // Stream download scope
            using (response)
            {
                await using (var contentStream = await response.Content.ReadAsStreamAsync(cts.Token))
                {
                    await using (var fileStream = new FileStream(partFile, existingBytes > 0 && isPartial ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        var buffer = new byte[81920];
                        int bytesRead;
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

                        await fileStream.FlushAsync(cts.Token);
                    } // fileStream closed and disposed here
                }
            }

            // Always compute SHA-256 hash for the downloaded file
            string? computedHash = null;
            bool? hashMatches = null;
            await using (var checkStream = File.OpenRead(partFile))
            {
                computedHash = await _hashVerifier.ComputeSha256Async(checkStream, cts.Token);
            }

            if (!string.IsNullOrEmpty(item.ExpectedSha256))
            {
                hashMatches = string.Equals(computedHash, item.ExpectedSha256.Trim(), StringComparison.OrdinalIgnoreCase);
                if (hashMatches == false)
                {
                    try { File.Delete(partFile); } catch { }
                    throw new InvalidOperationException($"SHA-256 hash mismatch! Computed: {computedHash}, Expected: {item.ExpectedSha256}");
                }
            }

            // Move from partFile to final LocalFilePath atomically
            if (File.Exists(item.LocalFilePath))
            {
                File.Delete(item.LocalFilePath);
            }
            File.Move(partFile, item.LocalFilePath);

            // Cleanup temp dir
            try { Directory.Delete(partDir, true); } catch { }

            // Check Authenticode Signature
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
            // Delete corrupt part file on unexpected failure
            try { if (File.Exists(partFile)) File.Delete(partFile); } catch { }
            try { if (Directory.Exists(partDir)) Directory.Delete(partDir, true); } catch { }

            if (_items.TryGetValue(downloadId, out var cur))
            {
                item = cur with { Status = DownloadStatus.Failed, ErrorMessage = ex.Message, SpeedBytesPerSec = 0 };
                _items[downloadId] = item;
                SaveQueue();
                DownloadProgressChanged?.Invoke(item);
            }
            throw;
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

            // Path containment verification before deleting
            if (File.Exists(item.LocalFilePath) && WindowsInstallerService.IsPathContainedWithin(item.LocalFilePath, _downloadDirectory))
            {
                try { File.Delete(item.LocalFilePath); } catch { }
            }

            var partDir = Path.Combine(_tempDirectory, downloadId);
            if (Directory.Exists(partDir))
            {
                try { Directory.Delete(partDir, true); } catch { }
            }

            SaveQueue();
            DownloadProgressChanged?.Invoke(_items[downloadId]);
        }
    }

    #region WinVerifyTrust Native Authenticode Inspection

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(
        IntPtr hwnd,
        [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID,
        ref WINTRUST_DATA pWVTData);

    private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 = new("{00AAC56B-CD44-11d0-8CC2-00C04FC295EE}");

    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_NONE = 0;
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_STATEACTION_IGNORE = 0;
    private const uint WTD_SAFER_FLAG = 0x00000100;

    public static bool IsKnownTrustedHost(string url, AppItem? app = null)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        var host = uri.Host.ToLowerInvariant();

        if (host == "github.com" || host.EndsWith(".github.com") || host.EndsWith(".githubusercontent.com") ||
            host == "gitlab.com" || host.EndsWith(".gitlab.com") ||
            host == "codeberg.org" || host.EndsWith(".codeberg.org"))
        {
            return true;
        }

        if (app != null)
        {
            if (!string.IsNullOrEmpty(app.OfficialSite) && Uri.TryCreate(app.OfficialSite, UriKind.Absolute, out var offUri) &&
                host.Equals(offUri.Host, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (!string.IsNullOrEmpty(app.RepoUrl) && Uri.TryCreate(app.RepoUrl, UriKind.Absolute, out var repUri) &&
                host.Equals(repUri.Host, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static AuthenticodeInfo CheckMsixSignature(string filePath)
    {
        try
        {
            using var archive = System.IO.Compression.ZipFile.OpenRead(filePath);
            var sigEntry = archive.GetEntry("AppxSignature.p7x");
            if (sigEntry == null)
            {
                return new AuthenticodeInfo(false, false, null, null, "Unsigned MSIX: No AppxSignature.p7x package signature found.");
            }

            using var entryStream = sigEntry.Open();
            using var ms = new MemoryStream();
            entryStream.CopyTo(ms);
            var sigBytes = ms.ToArray();

            var certCollection = new X509Certificate2Collection();
            certCollection.Import(sigBytes);

            if (certCollection.Count == 0)
            {
                return new AuthenticodeInfo(true, false, null, null, "MSIX signature present but no certificates could be parsed.");
            }

            var leafCert = certCollection[0];
            bool chainValid = leafCert.Verify();
            string statusMsg = chainValid
                ? "Verified: trusted MSIX package signature, publisher verified."
                : "MSIX package signature present but root certificate is untrusted or self-signed.";

            return new AuthenticodeInfo(true, chainValid, leafCert.Subject, leafCert.Issuer, statusMsg);
        }
        catch (Exception ex)
        {
            return new AuthenticodeInfo(false, false, null, null, $"MSIX signature check failed: {ex.Message}");
        }
    }

    public AuthenticodeInfo CheckAuthenticode(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new AuthenticodeInfo(false, false, null, null, "File not found.");
        }

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext is ".msix" or ".msixbundle" or ".appx" or ".appxbundle")
        {
            return CheckMsixSignature(filePath);
        }

        // 1. First run WinVerifyTrust P/Invoke to verify file digest integrity
        int trustResult = -1;
        try
        {
            var fileInfo = new WINTRUST_FILE_INFO
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
                pcwszFilePath = Path.GetFullPath(filePath),
                hFile = IntPtr.Zero,
                pgKnownSubject = IntPtr.Zero
            };

            var fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf(fileInfo));
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);

            try
            {
                var trustData = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                    pPolicyCallbackData = IntPtr.Zero,
                    pSIPClientData = IntPtr.Zero,
                    dwUIChoice = WTD_UI_NONE,
                    fdwRevocationChecks = WTD_REVOKE_NONE,
                    dwUnionChoice = WTD_CHOICE_FILE,
                    pFile = fileInfoPtr,
                    dwStateAction = WTD_STATEACTION_IGNORE,
                    hWVTStateData = IntPtr.Zero,
                    pwszURLReference = string.Empty,
                    dwProvFlags = WTD_SAFER_FLAG,
                    dwUIContext = 0,
                    pSignatureSettings = IntPtr.Zero
                };

                trustResult = WinVerifyTrust(IntPtr.Zero, WINTRUST_ACTION_GENERIC_VERIFY_V2, ref trustData);
            }
            finally
            {
                Marshal.FreeHGlobal(fileInfoPtr);
            }
        }
        catch (Exception ex)
        {
            return new AuthenticodeInfo(false, false, null, null, $"Authenticode verification error: {ex.Message}");
        }

        // 2. Extract certificate details if present
        string? subject = null;
        string? issuer = null;
        try
        {
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath));
            subject = cert.Subject;
            issuer = cert.Issuer;
        }
        catch
        {
            // Certificate could not be parsed
        }

        // Map WinVerifyTrust HRESULTs
        // 0 = Success (trusted and untampered)
        // 0x800B0100 = TRUST_E_NOSIGNATURE
        // 0x80096010 = TRUST_E_BAD_DIGEST (file tampered!)
        // 0x800B0109 = CERT_E_UNTRUSTEDROOT
        // 0x800B0101 = CERT_E_EXPIRED
        return trustResult switch
        {
            0 => new AuthenticodeInfo(true, true, subject, issuer, "Verified: trusted digital signature, file is authentic."),
            unchecked((int)0x800B0100) => new AuthenticodeInfo(false, false, null, null, "Unsigned: no embedded Authenticode digital signature found."),
            unchecked((int)0x80096010) => new AuthenticodeInfo(true, false, subject, issuer, "SECURITY WARNING: Digital signature invalid! File content does not match signature digest (potential tampering)."),
            unchecked((int)0x800B0109) => new AuthenticodeInfo(true, false, subject, issuer, "Digital signature present but root certificate is untrusted or self-signed."),
            unchecked((int)0x800B0101) => new AuthenticodeInfo(true, false, subject, issuer, "Digital signature present but certificate has expired."),
            _ => new AuthenticodeInfo(subject != null, false, subject, issuer, $"Digital signature verification returned status code 0x{trustResult:X8}.")
        };
    }

    #endregion
}
