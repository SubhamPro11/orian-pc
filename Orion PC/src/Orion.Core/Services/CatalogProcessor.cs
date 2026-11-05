using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Orion.Core.Models;

namespace Orion.Core.Services;

public interface ICatalogProcessor
{
    AppItem? SanitizeApp(JsonElement raw);
    AppItem ProcessItem(AppItem app, IReadOnlyDictionary<string, IReadOnlyList<ReleaseInfo>> mirror);
    IReadOnlyList<AppItem> ProcessCatalog(JsonElement rawApps, IReadOnlyDictionary<string, IReadOnlyList<ReleaseInfo>> mirror);
    IReadOnlyDictionary<string, IReadOnlyList<ReleaseInfo>> DecodeMirror(Stream stream);
    IReadOnlyList<AppItem> FilterByPlatform(IEnumerable<AppItem> apps, PlatformCategory category);
    IReadOnlyList<AppItem> Search(string query, string category, SortOption sort, IEnumerable<AppItem> apps);
    double ParseSize(string? s);
    string FormatSize(long? bytes);
}

public class CatalogProcessor : ICatalogProcessor
{
    private readonly IVersionService _versionService;
    private readonly IAssetResolver _assetResolver;

    private static readonly Regex RepoHostRegex = new(@"^https?://(www\.)?(github\.com|gitlab\.com|codeberg\.org)/", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex GhRepoRegex = new(@"github\.com/([^/\s]+/[^/\s#?]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public CatalogProcessor(IVersionService versionService, IAssetResolver assetResolver)
    {
        _versionService = versionService;
        _assetResolver = assetResolver;
    }

    public static string SanitizeUrl(string? u)
    {
        if (string.IsNullOrWhiteSpace(u)) return "#";
        var t = u.Trim();
        if (t.Equals("#", StringComparison.Ordinal)) return "#";
        if (Uri.TryCreate(t, UriKind.Absolute, out var uri) &&
            (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return uri.AbsoluteUri;
        }
        return "#";
    }

    public AppItem? SanitizeApp(JsonElement raw)
    {
        if (raw.ValueKind != JsonValueKind.Object) return null;

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
            };

            var app = JsonSerializer.Deserialize<AppItem>(raw.GetRawText(), options);
            if (app == null) return null;

            var id = string.IsNullOrEmpty(app.Id)
                ? (!string.IsNullOrEmpty(app.PackageName) ? app.PackageName : app.Name)
                : app.Id;

            var cat = string.IsNullOrWhiteSpace(app.Category) ? "Utility" : app.Category.Trim();
            if (cat.Length > 0)
            {
                cat = char.ToUpperInvariant(cat[0]) + (cat.Length > 1 ? cat[1..].ToLowerInvariant() : "");
            }

            var tags = (app.Tags ?? Array.Empty<string>()).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
            var plat = PlatformConstants.NormalizePlatform(app.Platform, tags);

            var icon = SanitizeUrl(app.Icon);
            if (icon == "#") icon = "";

            var dlUrl = SanitizeUrl(app.DownloadUrl);
            var screenshots = (app.Screenshots ?? Array.Empty<string>())
                .Select(SanitizeUrl)
                .Where(s => s != "#")
                .ToList();

            return app with
            {
                Id = id,
                Category = cat,
                Platform = plat,
                Tags = tags,
                Icon = icon,
                Version = string.IsNullOrEmpty(app.Version) ? "Latest" : app.Version,
                LatestVersion = string.IsNullOrEmpty(app.LatestVersion) ? "Latest" : app.LatestVersion,
                DownloadUrl = dlUrl,
                Screenshots = screenshots,
                AvailableVersions = Array.Empty<VersionOption>()
            };
        }
        catch
        {
            return null;
        }
    }

    public AppItem ProcessItem(AppItem app, IReadOnlyDictionary<string, IReadOnlyList<ReleaseInfo>> mirror)
    {
        var url = app.RepoUrl ?? string.Empty;
        var gh = !string.IsNullOrEmpty(app.GithubRepo) || url.Contains("github.com", StringComparison.OrdinalIgnoreCase);
        var gl = !string.IsNullOrEmpty(app.GitlabRepo) || url.Contains("gitlab", StringComparison.OrdinalIgnoreCase);
        var cb = !string.IsNullOrEmpty(app.CodebergRepo) || url.Contains("codeberg.org", StringComparison.OrdinalIgnoreCase);

        if (!gh && !gl && !cb) return app;

        string? key = null;
        if (!string.IsNullOrEmpty(app.GithubRepo))
        {
            key = $"github::github.com::{app.GithubRepo.ToLowerInvariant()}";
        }
        else if (!string.IsNullOrEmpty(app.GitlabRepo))
        {
            var domain = string.IsNullOrEmpty(app.GitlabDomain) ? "gitlab.com" : app.GitlabDomain;
            key = $"gitlab::{domain}::{app.GitlabRepo.ToLowerInvariant()}";
        }
        else if (!string.IsNullOrEmpty(app.CodebergRepo))
        {
            key = $"codeberg::codeberg.org::{app.CodebergRepo.ToLowerInvariant()}";
        }

        var clean = RepoHostRegex.Replace(url, "").TrimEnd('/').ToLowerInvariant();
        if (clean.EndsWith(".git")) clean = clean[..^4];

        IReadOnlyList<ReleaseInfo>? releases = null;
        if (key != null && mirror.TryGetValue(key.ToLowerInvariant(), out var r1)) releases = r1;
        else if (mirror.TryGetValue(clean, out var r2)) releases = r2;
        else if (!string.IsNullOrEmpty(app.GithubRepo) && mirror.TryGetValue(app.GithubRepo.ToLowerInvariant(), out var r3)) releases = r3;
        else if (!string.IsNullOrEmpty(app.GitlabRepo) && mirror.TryGetValue(app.GitlabRepo.ToLowerInvariant(), out var r4)) releases = r4;
        else if (!string.IsNullOrEmpty(app.CodebergRepo) && mirror.TryGetValue(app.CodebergRepo.ToLowerInvariant(), out var r5)) releases = r5;

        if (releases == null || releases.Count == 0) return app;

        var kw = string.IsNullOrWhiteSpace(app.ReleaseKeyword) ? null : app.ReleaseKeyword.Trim().ToLowerInvariant();
        var nowStr = DateTime.UtcNow.ToString("yyyy-MM-dd");

        var versions = new List<(VersionOption Option, long DateMillis)>();

        foreach (var r in releases)
        {
            var candidates = r.Assets;
            if (kw != null)
            {
                var filtered = candidates.Where(a => a.Name.Contains(kw, StringComparison.OrdinalIgnoreCase) || a.DownloadUrl.Contains(kw, StringComparison.OrdinalIgnoreCase)).ToList();
                if (filtered.Count > 0) candidates = filtered;
                else if (!r.Name.Contains(kw, StringComparison.OrdinalIgnoreCase) && !r.TagName.Contains(kw, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            if (candidates.Count == 0) continue;

            var stream = _versionService.DetectStream(candidates[0].Name, r.TagName, r.Prerelease);
            var tagV = r.TagName.Equals("latest", StringComparison.OrdinalIgnoreCase) || r.TagName.Equals("all", StringComparison.OrdinalIgnoreCase)
                ? null
                : _versionService.ExtractVersion(r.TagName);

            var ver = _versionService.ExtractVersion(candidates[0].Name)
                      ?? tagV
                      ?? _versionService.ExtractVersion(r.Name)
                      ?? (r.PublishedAt.Length >= 10 ? r.PublishedAt[..10] : nowStr);

            var variants = candidates.Select(a =>
            {
                var arch = _assetResolver.DetermineArchitecture(a.Name).ToString();
                var pkgType = _assetResolver.DeterminePackageType(a.Name, a.DownloadUrl);
                return new AppVariant(arch, a.DownloadUrl, a.Size, pkgType, a.Sha256);
            })
            .OrderByDescending(v => _assetResolver.IsWindowsPackage(v.PackageType))
            .ThenBy(v => v.Arch switch
            {
                "Universal" => 1,
                "X64" => 2,
                "Arm64" => 3,
                "X86" => 4,
                _ => 5
            })
            .ToList();

            var dateStr = r.PublishedAt.Length >= 10 ? r.PublishedAt[..10] : nowStr;
            long dateMs = DateTimeOffset.TryParse(r.PublishedAt, out var dto) ? dto.ToUnixTimeMilliseconds() : 0;

            versions.Add((new VersionOption(stream, ver, dateStr, variants, r.TagName), dateMs));
        }

        if (versions.Count == 0) return app;

        var streams = new[] { "Stable", "Beta", "Alpha", "Nightly" };
        var latestByStream = new List<VersionOption>();
        foreach (var st in streams)
        {
            var match = versions.Where(v => v.Option.Type.Equals(st, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(v => v.DateMillis)
                .Select(v => v.Option)
                .FirstOrDefault();
            if (match != null) latestByStream.Add(match);
        }

        if (latestByStream.Count == 0) latestByStream.Add(versions[0].Option);

        var def = latestByStream.FirstOrDefault(v => v.Type.Equals("Stable", StringComparison.OrdinalIgnoreCase)) ?? latestByStream[0];
        var v0 = def.VariantsList.FirstOrDefault();

        return app with
        {
            Version = def.Version,
            LatestVersion = def.Version,
            DownloadUrl = v0 != null && !string.IsNullOrEmpty(v0.Url) ? v0.Url : "#",
            Variants = def.VariantsList,
            AvailableVersions = latestByStream,
            Size = v0?.Size != null ? FormatSize(v0.Size) : app.Size
        };
    }

    public IReadOnlyList<AppItem> ProcessCatalog(JsonElement rawApps, IReadOnlyDictionary<string, IReadOnlyList<ReleaseInfo>> mirror)
    {
        if (rawApps.ValueKind != JsonValueKind.Array) return Array.Empty<AppItem>();

        var result = new List<AppItem>();
        foreach (var element in rawApps.EnumerateArray())
        {
            var sanitized = SanitizeApp(element);
            if (sanitized != null)
            {
                result.Add(ProcessItem(sanitized, mirror));
            }
        }
        return result;
    }

    public IReadOnlyDictionary<string, IReadOnlyList<ReleaseInfo>> DecodeMirror(Stream stream)
    {
        using var doc = JsonDocument.Parse(stream);
        var result = new Dictionary<string, IReadOnlyList<ReleaseInfo>>(StringComparer.OrdinalIgnoreCase);

        if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var key = prop.Name.ToLowerInvariant();
            var releases = new List<ReleaseInfo>();

            if (prop.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var rObj in prop.Value.EnumerateArray())
                {
                    var parsed = ParseRelease(rObj);
                    if (parsed != null) releases.Add(parsed);
                }
            }
            else if (prop.Value.ValueKind == JsonValueKind.Object)
            {
                var parsed = ParseRelease(prop.Value);
                if (parsed != null) releases.Add(parsed);
            }

            if (releases.Count > 0)
            {
                result[key] = releases;
            }
        }

        return result;
    }

    private static ReleaseInfo? ParseRelease(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;

        string tagName = element.TryGetProperty("tag_name", out var tn) ? tn.GetString() ?? "" : "";
        string name = element.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        bool prerelease = element.TryGetProperty("prerelease", out var pr) && pr.GetBoolean();
        string publishedAt = element.TryGetProperty("published_at", out var pa) ? pa.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(publishedAt) && element.TryGetProperty("created_at", out var ca))
        {
            publishedAt = ca.GetString() ?? "";
        }

        var assets = new List<ReleaseAssetInfo>();
        if (element.TryGetProperty("assets", out var aArr) && aArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in aArr.EnumerateArray())
            {
                string aName = a.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "";
                string aUrl = a.TryGetProperty("browser_download_url", out var au) ? au.GetString() ?? "" : "";
                long? aSize = a.TryGetProperty("size", out var asz) ? asz.GetInt64() : null;
                string? aType = a.TryGetProperty("content_type", out var at) ? at.GetString() : null;
                string? aSha256 = null;
                if (a.TryGetProperty("sha256", out var ash)) aSha256 = ash.GetString();
                else if (a.TryGetProperty("digest", out var adg)) aSha256 = adg.GetString();
                else if (a.TryGetProperty("hash", out var ah)) aSha256 = ah.GetString();

                if (!string.IsNullOrEmpty(aName) && !string.IsNullOrEmpty(aUrl))
                {
                    assets.Add(new ReleaseAssetInfo(aName, aUrl, aSize, aType, aSha256));
                }
            }
        }

        return new ReleaseInfo(tagName, name, prerelease, publishedAt, assets);
    }

    public IReadOnlyList<AppItem> FilterByPlatform(IEnumerable<AppItem> apps, PlatformCategory category)
    {
        return apps.Where(a => PlatformConstants.MatchesCategory(a.Platform, a.TagsList, category)).ToList();
    }

    public IReadOnlyList<AppItem> Search(string query, string category, SortOption sort, IEnumerable<AppItem> apps)
    {
        var t = (query ?? "").Trim().ToLowerInvariant();
        IEnumerable<AppItem> list;

        if (string.IsNullOrEmpty(t))
        {
            list = apps;
        }
        else
        {
            list = apps
                .Select(a =>
                {
                    int score = 0;
                    var name = a.Name.ToLowerInvariant();
                    if (name.Contains(t)) score += 10;
                    if (name.StartsWith(t)) score += 5;
                    if (a.Author.Contains(t, StringComparison.OrdinalIgnoreCase)) score += 5;
                    if (a.Description.Contains(t, StringComparison.OrdinalIgnoreCase)) score += 1;
                    if (a.Id.Contains(t, StringComparison.OrdinalIgnoreCase)) score += 2;
                    return (App: a, Score: score);
                })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .Select(x => x.App);
        }

        if (!string.IsNullOrEmpty(category) && !category.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            list = list.Where(a => a.Category.Equals(category, StringComparison.OrdinalIgnoreCase) ||
                                   a.TagsList.Any(tag => tag.Equals(category, StringComparison.OrdinalIgnoreCase)));
        }

        var result = list.ToList();

        return sort switch
        {
            SortOption.NameAsc => result.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            SortOption.NameDesc => result.OrderByDescending(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            SortOption.SizeAsc => result.OrderBy(a => ParseSize(a.Size)).ToList(),
            SortOption.SizeDesc => result.OrderByDescending(a => ParseSize(a.Size)).ToList(),
            SortOption.Newest => string.IsNullOrEmpty(t) ? result.AsEnumerable().Reverse().ToList() : result,
            _ => result
        };
    }

    public double ParseSize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0.0;
        var l = s.ToLowerInvariant();
        if (l.Contains("varies")) return 0.0;

        var numStr = Regex.Replace(l, @"[^0-9.]", "");
        if (!double.TryParse(numStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var n)) return 0.0;

        if (l.Contains("gb")) return n * 1024 * 1024 * 1024;
        if (l.Contains("mb")) return n * 1024 * 1024;
        if (l.Contains("kb")) return n * 1024;
        return n;
    }

    public string FormatSize(long? bytes)
    {
        if (bytes == null || bytes <= 0) return "Varies";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int i = Math.Clamp((int)Math.Floor(Math.Log(bytes.Value) / Math.Log(1024.0)), 0, 4);
        double v = Math.Round(bytes.Value / Math.Pow(1024.0, i), 1);
        return v % 1.0 == 0.0 ? $"{(long)v} {units[i]}" : $"{v.ToString("0.0", CultureInfo.InvariantCulture)} {units[i]}";
    }
}

