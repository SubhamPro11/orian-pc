using System.Text.Json;

namespace Orion.Platform.Windows.Services;

public record VtScanStats(
    int Harmless,
    int Malicious,
    int Suspicious,
    int Undetected,
    int Timeout
)
{
    public int Flagged => Malicious + Suspicious;
    public int Total => Harmless + Malicious + Suspicious + Undetected + Timeout;
    public string Verdict => Flagged == 0 && Total > 0 ? "Safe" : Flagged > 0 ? "Caution" : "Unknown";
}

public record VtScanResult(
    string Hash,
    VtScanStats Stats,
    string Permalink,
    bool Found
);

public interface IVirusTotalService
{
    Task<VtScanResult?> GetFileReportAsync(string sha256Hash, string? apiKey = null, CancellationToken cancellationToken = default);
}

public class VirusTotalService : IVirusTotalService
{
    private readonly HttpClient _httpClient;
    private readonly ISecureStorageService _secureStorage;

    public VirusTotalService(HttpClient httpClient, ISecureStorageService secureStorage)
    {
        _httpClient = httpClient;
        _secureStorage = secureStorage;
    }

    public async Task<VtScanResult?> GetFileReportAsync(
        string sha256Hash,
        string? apiKey = null,
        CancellationToken cancellationToken = default)
    {
        var key = apiKey ?? _secureStorage.GetSecret("VirusTotalApiKey");
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var request = new HttpRequestMessage(HttpMethod.Get, $"https://www.virustotal.com/api/v3/files/{sha256Hash}");
        request.Headers.Add("x-apikey", key.Trim());

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new VtScanResult(sha256Hash, new VtScanStats(0, 0, 0, 0, 0), $"https://www.virustotal.com/gui/file/{sha256Hash}", false);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"VirusTotal API returned status {response.StatusCode}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("attributes", out var attr))
        {
            return null;
        }

        var stats = new VtScanStats(0, 0, 0, 0, 0);
        if (attr.TryGetProperty("last_analysis_stats", out var st))
        {
            stats = new VtScanStats(
                st.TryGetProperty("harmless", out var h) ? h.GetInt32() : 0,
                st.TryGetProperty("malicious", out var m) ? m.GetInt32() : 0,
                st.TryGetProperty("suspicious", out var s) ? s.GetInt32() : 0,
                st.TryGetProperty("undetected", out var u) ? u.GetInt32() : 0,
                st.TryGetProperty("timeout", out var t) ? t.GetInt32() : 0
            );
        }

        return new VtScanResult(
            sha256Hash,
            stats,
            $"https://www.virustotal.com/gui/file/{sha256Hash}",
            true
        );
    }
}

