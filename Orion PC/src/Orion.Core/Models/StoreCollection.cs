namespace Orion.Core.Models;

public record CategoryCard(
    string Id,
    string Label,
    string Icon,
    int Count,
    string Accent,
    IReadOnlyList<string> Gradient
);

public record BundleItem(
    string Id,
    string Title,
    string Description,
    IReadOnlyList<string> AppIds,
    IReadOnlyList<AppItem> Apps,
    string? Color = null,
    string? Badge = null,
    string? Icon = null,
    string? Monogram = null
);

public record StoreCollection(
    string Id,
    string Title,
    CollectionType Type,
    string? Subtitle = null,
    string? Filter = null,
    string? Animation = null,
    IReadOnlyList<AppItem>? Apps = null,
    IReadOnlyList<BundleItem>? Bundles = null,
    IReadOnlyList<CategoryCard>? CategoryCards = null,
    int? TotalAppCount = null
)
{
    public IReadOnlyList<AppItem> AppsList => Apps ?? Array.Empty<AppItem>();
    public IReadOnlyList<BundleItem> BundlesList => Bundles ?? Array.Empty<BundleItem>();
    public IReadOnlyList<CategoryCard> CategoryCardsList => CategoryCards ?? Array.Empty<CategoryCard>();
}

public record ReleaseAssetInfo(
    string Name,
    string DownloadUrl,
    long? Size,
    string? ContentType = null
);

public record ReleaseInfo(
    string TagName,
    string Name,
    bool Prerelease,
    string PublishedAt,
    IReadOnlyList<ReleaseAssetInfo> Assets
);

public static class Defaults
{
    public const string ReleasesApi = "https://api.github.com/repos/RookieEnough/Orion-Store/releases?per_page=8";
    public const string MirrorJson = "https://raw.githubusercontent.com/RookieEnough/Orion-Data/data/mirror.json";
    public const string StarsJson = "https://raw.githubusercontent.com/RookieEnough/Orion-Data/data/stars.json";

    public static readonly IReadOnlyList<string> ConfigUrls = new[]
    {
        "https://raw.githubusercontent.com/RookieEnough/Orion-Data/main/config.json",
        "https://cdn.jsdelivr.net/gh/RookieEnough/Orion-Data@main/config.json",
        "https://raw.githack.com/RookieEnough/Orion-Data/main/config.json"
    };

    public static readonly IReadOnlyList<string> AppsUrls = ConfigUrls
        .Select(u => u.Replace("config.json", "apps.json"))
        .ToArray();

    public static readonly IReadOnlyList<string> MirrorLabels = new[] { "GitHub", "JSDelivr", "GitHack" };

    public const string SupportEmail = "orionstoredev@gmail.com";
    public const string AppId = "com.orion.store.pc";
}

