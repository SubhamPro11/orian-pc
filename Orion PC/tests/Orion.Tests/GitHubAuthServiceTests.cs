using System.Net;
using System.Text.Json;
using Orion.Core.Models;
using Orion.Platform.Windows.Services;
using Xunit;

namespace Orion.Tests;

public class GitHubAuthServiceTests
{
    private class FakeSecureStorage : ISecureStorageService
    {
        private readonly Dictionary<string, string> _store = new(StringComparer.OrdinalIgnoreCase);

        public void SetSecret(string key, string secret) => _store[key] = secret;
        public string? GetSecret(string key) => _store.TryGetValue(key, out var s) ? s : null;
        public void RemoveSecret(string key) => _store.Remove(key);
    }

    [Fact]
    public void GitHubUser_DeserializesProperlyFromJson()
    {
        var json = """
        {
            "login": "octocat",
            "id": 583231,
            "name": "The Octocat",
            "avatar_url": "https://avatars.githubusercontent.com/u/583231?v=4",
            "html_url": "https://github.com/octocat",
            "email": "octocat@github.com",
            "bio": "Building the future of software.",
            "company": "@github",
            "location": "San Francisco",
            "public_repos": 8,
            "followers": 15000
        }
        """;

        var user = JsonSerializer.Deserialize<GitHubUser>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(user);
        Assert.Equal("octocat", user.Login);
        Assert.Equal(583231, user.Id);
        Assert.Equal("The Octocat", user.Name);
        Assert.Equal("https://avatars.githubusercontent.com/u/583231?v=4", user.AvatarUrl);
        Assert.Equal("https://github.com/octocat", user.HtmlUrl);
        Assert.Equal("octocat@github.com", user.Email);
        Assert.Equal("Building the future of software.", user.Bio);
        Assert.Equal("@github", user.Company);
        Assert.Equal("San Francisco", user.Location);
        Assert.Equal(8, user.PublicRepos);
        Assert.Equal(15000, user.Followers);
    }

    [Fact]
    public void GitHubAuthService_Constants_MatchProvidedCredentials()
    {
        Assert.Equal("Ov23liqwY8lYdNTpkz9i", GitHubAuthService.ClientId);
        Assert.Equal("993daa9536df434fc42a1577435f6b4836013128", GitHubAuthService.ClientSecret);
        Assert.Equal("http://localhost:52134/callback/", GitHubAuthService.DefaultRedirectUri);
    }

    [Fact]
    public void GitHubAuthService_Logout_ClearsAuthStateAndSecrets()
    {
        var storage = new FakeSecureStorage();
        storage.SetSecret("GitHubAccessToken", "ghu_dummy_token_123");
        storage.SetSecret("GitHubCachedUser", """{"login":"testuser","id":123}""");

        var service = new GitHubAuthService(new HttpClient(), storage);
        _ = service.InitializeAsync();

        GitHubUser? notifiedUser = new GitHubUser("temp", 1);
        service.AuthStateChanged += u => notifiedUser = u;

        service.Logout();

        Assert.False(service.IsAuthenticated);
        Assert.Null(service.CurrentUser);
        Assert.Null(service.AccessToken);
        Assert.Null(storage.GetSecret("GitHubAccessToken"));
        Assert.Null(storage.GetSecret("GitHubCachedUser"));
        Assert.Null(notifiedUser);
    }

    [Fact]
    public async Task GitHubAuthService_LoginWithToken_RejectsEmptyOrWhitespaceToken()
    {
        var storage = new FakeSecureStorage();
        var service = new GitHubAuthService(new HttpClient(), storage);

        var resultEmpty = await service.LoginWithTokenAsync("");
        Assert.False(resultEmpty);
        Assert.False(service.IsAuthenticated);

        var resultWhitespace = await service.LoginWithTokenAsync("   ");
        Assert.False(resultWhitespace);
        Assert.False(service.IsAuthenticated);
    }
}
