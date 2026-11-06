using System.Text.Json.Serialization;

namespace Orion.Core.Models;

public record GitHubUser(
    [property: JsonPropertyName("login")] string Login,
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("avatar_url")] string? AvatarUrl = null,
    [property: JsonPropertyName("html_url")] string? HtmlUrl = null,
    [property: JsonPropertyName("email")] string? Email = null,
    [property: JsonPropertyName("bio")] string? Bio = null,
    [property: JsonPropertyName("company")] string? Company = null,
    [property: JsonPropertyName("location")] string? Location = null,
    [property: JsonPropertyName("public_repos")] int PublicRepos = 0,
    [property: JsonPropertyName("followers")] int Followers = 0
);
