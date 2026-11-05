using System.Text.Json.Serialization;

namespace Orion.Core.Models;

public record AppVariant(
    [property: JsonPropertyName("arch")] string Arch = "",
    [property: JsonPropertyName("url")] string Url = "",
    [property: JsonPropertyName("size")] long? Size = null,
    [property: JsonPropertyName("packageType")] PackageType PackageType = PackageType.Unknown
);

public record VersionOption(
    [property: JsonPropertyName("type")] string Type = "Stable",
    [property: JsonPropertyName("version")] string Version = "",
    [property: JsonPropertyName("date")] string Date = "",
    [property: JsonPropertyName("variants")] IReadOnlyList<AppVariant>? Variants = null,
    [property: JsonPropertyName("tag")] string Tag = ""
)
{
    public IReadOnlyList<AppVariant> VariantsList => Variants ?? Array.Empty<AppVariant>();
}

public record AppItem(
    [property: JsonPropertyName("id")] string Id = "",
    [property: JsonPropertyName("name")] string Name = "Unknown App",
    [property: JsonPropertyName("description")] string Description = "",
    [property: JsonPropertyName("icon")] string Icon = "",
    [property: JsonPropertyName("version")] string Version = "Latest",
    [property: JsonPropertyName("latestVersion")] string LatestVersion = "Latest",
    [property: JsonPropertyName("downloadUrl")] string DownloadUrl = "#",
    [property: JsonPropertyName("variants")] IReadOnlyList<AppVariant>? Variants = null,
    [property: JsonPropertyName("availableVersions")] IReadOnlyList<VersionOption>? AvailableVersions = null,
    [property: JsonPropertyName("repoUrl")] string? RepoUrl = null,
    [property: JsonPropertyName("githubRepo")] string? GithubRepo = null,
    [property: JsonPropertyName("gitlabRepo")] string? GitlabRepo = null,
    [property: JsonPropertyName("codebergRepo")] string? CodebergRepo = null,
    [property: JsonPropertyName("gitlabDomain")] string? GitlabDomain = null,
    [property: JsonPropertyName("releaseKeyword")] string? ReleaseKeyword = null,
    [property: JsonPropertyName("packageName")] string? PackageName = null,
    [property: JsonPropertyName("category")] string Category = "Utility",
    [property: JsonPropertyName("tags")] IReadOnlyList<string>? Tags = null,
    [property: JsonPropertyName("platform")] string Platform = PlatformConstants.Android,
    [property: JsonPropertyName("size")] string Size = "",
    [property: JsonPropertyName("author")] string Author = "Unknown",
    [property: JsonPropertyName("screenshots")] IReadOnlyList<string>? Screenshots = null,
    [property: JsonPropertyName("officialSite")] string? OfficialSite = null,
    [property: JsonPropertyName("patches")] IReadOnlyList<string>? Patches = null,
    [property: JsonPropertyName("stars")] int? Stars = null
)
{
    public IReadOnlyList<AppVariant> VariantsList => Variants ?? Array.Empty<AppVariant>();
    public IReadOnlyList<VersionOption> AvailableVersionsList => AvailableVersions ?? Array.Empty<VersionOption>();
    public IReadOnlyList<string> TagsList => Tags ?? Array.Empty<string>();
    public IReadOnlyList<string> ScreenshotsList => Screenshots ?? Array.Empty<string>();
    public IReadOnlyList<string> PatchesList => Patches ?? Array.Empty<string>();

    public bool IsWindowsApp => PlatformConstants.NormalizePlatform(Platform, TagsList) == PlatformConstants.PC;
}

