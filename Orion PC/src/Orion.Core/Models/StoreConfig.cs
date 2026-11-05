using System.Text.Json.Serialization;

namespace Orion.Core.Models;

public record SocialLinks(
    [property: JsonPropertyName("github")] string Github = "",
    [property: JsonPropertyName("x")] string X = "",
    [property: JsonPropertyName("discord")] string Discord = "",
    [property: JsonPropertyName("coffee")] string Coffee = ""
);

public record DonationConfig(
    [property: JsonPropertyName("kofiUrl")] string? KofiUrl = null,
    [property: JsonPropertyName("upiId")] string? UpiId = null,
    [property: JsonPropertyName("paypalUrl")] string? PaypalUrl = null
);

public record DevProfile(
    [property: JsonPropertyName("name")] string Name = "",
    [property: JsonPropertyName("image")] string? Image = null,
    [property: JsonPropertyName("bio")] string Bio = ""
);

public record FAQItem(
    [property: JsonPropertyName("question")] string Question = "",
    [property: JsonPropertyName("answer")] string Answer = "",
    [property: JsonPropertyName("icon")] string Icon = ""
);

public record Notice(
    [property: JsonPropertyName("id")] string Id = "",
    [property: JsonPropertyName("title")] string Title = "",
    [property: JsonPropertyName("message")] string Message = "",
    [property: JsonPropertyName("show")] bool Show = false
);

public record PillConfig(
    [property: JsonPropertyName("id")] string? Id = null,
    [property: JsonPropertyName("label")] string? Label = null,
    [property: JsonPropertyName("appId")] string? AppId = null,
    [property: JsonPropertyName("tone")] string? Tone = null
);

public record ModuleBundleConfig(
    [property: JsonPropertyName("id")] string Id = "",
    [property: JsonPropertyName("title")] string Title = "",
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("appIds")] IReadOnlyList<string>? AppIds = null,
    [property: JsonPropertyName("color")] string? Color = null,
    [property: JsonPropertyName("badge")] string? Badge = null,
    [property: JsonPropertyName("icon")] string? Icon = null,
    [property: JsonPropertyName("monogram")] string? Monogram = null
)
{
    public IReadOnlyList<string> AppIdsList => AppIds ?? Array.Empty<string>();
}

public record ModuleConfig(
    [property: JsonPropertyName("id")] string Id = "",
    [property: JsonPropertyName("type")] string Type = "",
    [property: JsonPropertyName("title")] string Title = "",
    [property: JsonPropertyName("subtitle")] string? Subtitle = null,
    [property: JsonPropertyName("platform")] string? Platform = null,
    [property: JsonPropertyName("insertAfterCategory")] double? InsertAfterCategory = null,
    [property: JsonPropertyName("animation")] string? Animation = null,
    [property: JsonPropertyName("appIds")] IReadOnlyList<string>? AppIds = null,
    [property: JsonPropertyName("pills")] IReadOnlyList<PillConfig>? Pills = null,
    [property: JsonPropertyName("bundles")] IReadOnlyList<ModuleBundleConfig>? Bundles = null
)
{
    public IReadOnlyList<string> AppIdsList => AppIds ?? Array.Empty<string>();
    public IReadOnlyList<PillConfig> PillsList => Pills ?? Array.Empty<PillConfig>();
    public IReadOnlyList<ModuleBundleConfig> BundlesList => Bundles ?? Array.Empty<ModuleBundleConfig>();
}

public record StoreConfig(
    [property: JsonPropertyName("appsJsonUrl")] string AppsJsonUrl = "",
    [property: JsonPropertyName("mirrorJsonUrl")] string? MirrorJsonUrl = null,
    [property: JsonPropertyName("maintenanceMode")] bool MaintenanceMode = false,
    [property: JsonPropertyName("maintenanceMessage")] string? MaintenanceMessage = null,
    [property: JsonPropertyName("announcement")] string? Announcement = null,
    [property: JsonPropertyName("notice")] Notice? Notice = null,
    [property: JsonPropertyName("minStoreVersion")] string? MinStoreVersion = null,
    [property: JsonPropertyName("latestStoreVersion")] string? LatestStoreVersion = null,
    [property: JsonPropertyName("storeDownloadUrl")] string? StoreDownloadUrl = null,
    [property: JsonPropertyName("socials")] SocialLinks? SocialLinks = null,
    [property: JsonPropertyName("donation")] DonationConfig? Donation = null,
    [property: JsonPropertyName("devProfile")] DevProfile? DevProfile = null,
    [property: JsonPropertyName("faqs")] IReadOnlyList<FAQItem>? Faqs = null,
    [property: JsonPropertyName("supportEmail")] string? SupportEmail = null,
    [property: JsonPropertyName("easterEggUrl")] string? EasterEggUrl = null,
    [property: JsonPropertyName("leaderboardUrl")] string? LeaderboardUrl = null,
    [property: JsonPropertyName("storefrontModules")] IReadOnlyList<ModuleConfig>? StorefrontModules = null
)
{
    public IReadOnlyList<FAQItem> FaqsList => Faqs ?? Array.Empty<FAQItem>();
    public IReadOnlyList<ModuleConfig> StorefrontModulesList => StorefrontModules ?? Array.Empty<ModuleConfig>();
}

