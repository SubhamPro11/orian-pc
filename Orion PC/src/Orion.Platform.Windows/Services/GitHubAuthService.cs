using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Orion.Core.Models;

namespace Orion.Platform.Windows.Services;

public interface IGitHubAuthService
{
    bool IsAuthenticated { get; }
    GitHubUser? CurrentUser { get; }
    string? AccessToken { get; }
    string? AuthError { get; }
    bool IsBusy { get; }

    event Action<GitHubUser?>? AuthStateChanged;

    Task<bool> InitializeAsync();
    Task<bool> LoginWithBrowserAsync(CancellationToken cancellationToken = default);
    Task<bool> LoginWithTokenAsync(string token, CancellationToken cancellationToken = default);
    Task<bool> CompleteAuthWithCodeAsync(string code, string? redirectUri = null, CancellationToken cancellationToken = default);
    Task<GitHubUser?> FetchUserProfileAsync(string token, CancellationToken cancellationToken = default);
    void Logout();
}

public class GitHubAuthService : IGitHubAuthService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public const string ClientId = "Ov23liqwY8lYdNTpkz9i";
    public const string ClientSecret = "993daa9536df434fc42a1577435f6b4836013128";
    public const string DefaultRedirectUri = "http://localhost:52134/callback/";

    private readonly HttpClient _httpClient;
    private readonly ISecureStorageService _secureStorage;
    private readonly Action<string>? _logInfo;
    private readonly Action<string, Exception?>? _logError;

    private GitHubUser? _currentUser;
    private string? _accessToken;
    private string? _authError;
    private bool _isBusy;

    public bool IsAuthenticated => _currentUser != null && !string.IsNullOrEmpty(_accessToken);
    public GitHubUser? CurrentUser => _currentUser;
    public string? AccessToken => _accessToken;
    public string? AuthError
    {
        get => _authError;
        private set => _authError = value;
    }
    public bool IsBusy
    {
        get => _isBusy;
        private set => _isBusy = value;
    }

    public event Action<GitHubUser?>? AuthStateChanged;

    public GitHubAuthService(
        HttpClient httpClient,
        ISecureStorageService secureStorage,
        Action<string>? logInfo = null,
        Action<string, Exception?>? logError = null)
    {
        _httpClient = httpClient;
        _secureStorage = secureStorage;
        _logInfo = logInfo;
        _logError = logError;
    }

    public async Task<bool> InitializeAsync()
    {
        try
        {
            var storedToken = _secureStorage.GetSecret("GitHubAccessToken");
            if (string.IsNullOrWhiteSpace(storedToken))
            {
                return false;
            }

            _accessToken = storedToken.Trim();

            // Load fast cached profile if present
            var cachedJson = _secureStorage.GetSecret("GitHubCachedUser");
            if (!string.IsNullOrWhiteSpace(cachedJson))
            {
                try
                {
                    _currentUser = JsonSerializer.Deserialize<GitHubUser>(cachedJson, JsonOptions);
                    if (_currentUser != null)
                    {
                        AuthStateChanged?.Invoke(_currentUser);
                    }
                }
                catch { }
            }

            // Verify and refresh profile in background
            _ = Task.Run(async () =>
            {
                try
                {
                    var liveUser = await FetchUserProfileAsync(_accessToken).ConfigureAwait(false);
                    if (liveUser != null)
                    {
                        _currentUser = liveUser;
                        _secureStorage.SetSecret("GitHubCachedUser", JsonSerializer.Serialize(liveUser));
                        AuthStateChanged?.Invoke(_currentUser);
                    }
                    else
                    {
                        // Token expired or revoked
                        _logInfo?.Invoke("GitHub token verification returned null. Clearing credentials.");
                        Logout();
                    }
                }
                catch (Exception ex)
                {
                    _logError?.Invoke("Background GitHub profile check failed", ex);
                }
            });

            return true;
        }
        catch (Exception ex)
        {
            _logError?.Invoke("Failed to initialize GitHub auth state", ex);
            return false;
        }
    }

    public async Task<bool> LoginWithBrowserAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        AuthError = null;

        HttpListener? listener = null;
        var state = Guid.NewGuid().ToString("N");

        try
        {
            listener = new HttpListener();
            listener.Prefixes.Add(DefaultRedirectUri);
            listener.Start();

            var authUrl = $"https://github.com/login/oauth/authorize?client_id={ClientId}&redirect_uri={Uri.EscapeDataString(DefaultRedirectUri)}&scope=read:user%20user:email&state={state}";

            Process.Start(new ProcessStartInfo
            {
                FileName = authUrl,
                UseShellExecute = true
            });

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(2));

            var contextTask = listener.GetContextAsync();
            var completedTask = await Task.WhenAny(contextTask, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);

            if (completedTask != contextTask)
            {
                throw new OperationCanceledException("Sign in timed out or was cancelled by user.");
            }

            var context = await contextTask.ConfigureAwait(false);
            var req = context.Request;
            var resp = context.Response;

            var query = req.QueryString;
            var code = query["code"];
            var returnedState = query["state"];
            var error = query["error"];
            var errorDesc = query["error_description"];

            string responseHtml;
            if (!string.IsNullOrEmpty(error))
            {
                responseHtml = $@"<!DOCTYPE html><html><head><title>Orion Store - Sign In Failed</title><style>body {{ font-family: -apple-system, sans-serif; background: #18181b; color: #fff; text-align: center; padding-top: 80px; }} h2 {{ color: #ef4444; }}</style></head><body><h2>Sign in was not completed</h2><p>{WebUtility.HtmlEncode(errorDesc ?? error)}</p><p>You can close this tab and return to Orion Store.</p></body></html>";
            }
            else
            {
                responseHtml = @"<!DOCTYPE html><html><head><title>Orion Store - Signed In</title><style>body { font-family: -apple-system, sans-serif; background: #18181b; color: #fff; text-align: center; padding-top: 80px; } h2 {{ color: #10b981; }}</style></head><body><h2>Successfully connected to GitHub!</h2><p>You can close this tab and return to Orion Store for PC.</p></body></html>";
            }

            var buffer = Encoding.UTF8.GetBytes(responseHtml);
            resp.ContentType = "text/html; charset=utf-8";
            resp.ContentLength64 = buffer.Length;
            await resp.OutputStream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            resp.OutputStream.Close();

            if (!string.IsNullOrEmpty(error))
            {
                AuthError = errorDesc ?? error;
                return false;
            }

            if (returnedState != state)
            {
                AuthError = "Security verification failed: OAuth state mismatch.";
                return false;
            }

            if (string.IsNullOrEmpty(code))
            {
                AuthError = "No authorization code received from GitHub.";
                return false;
            }

            return await CompleteAuthWithCodeAsync(code, DefaultRedirectUri, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AuthError = ex.Message;
            _logError?.Invoke("Browser OAuth failed", ex);
            return false;
        }
        finally
        {
            try
            {
                listener?.Stop();
                listener?.Close();
            }
            catch { }
            IsBusy = false;
        }
    }

    public async Task<bool> CompleteAuthWithCodeAsync(string code, string? redirectUri = null, CancellationToken cancellationToken = default)
    {
        try
        {
            using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token");
            tokenRequest.Headers.Accept.ParseAdd("application/json");
            tokenRequest.Headers.UserAgent.ParseAdd("OrionStorePC/1.0");

            var pairs = new List<KeyValuePair<string, string>>
            {
                new("client_id", ClientId),
                new("client_secret", ClientSecret),
                new("code", code.Trim())
            };
            if (!string.IsNullOrEmpty(redirectUri))
            {
                pairs.Add(new("redirect_uri", redirectUri));
            }

            tokenRequest.Content = new FormUrlEncodedContent(pairs);

            using var resp = await _httpClient.SendAsync(tokenRequest, cancellationToken).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                AuthError = $"GitHub token exchange failed (HTTP {(int)resp.StatusCode}).";
                return false;
            }

            var json = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var errProp))
            {
                var desc = doc.RootElement.TryGetProperty("error_description", out var descProp) ? descProp.GetString() : errProp.GetString();
                AuthError = $"GitHub returned error: {desc}";
                return false;
            }

            if (!doc.RootElement.TryGetProperty("access_token", out var tokenProp) || string.IsNullOrEmpty(tokenProp.GetString()))
            {
                AuthError = "No access token in GitHub response.";
                return false;
            }

            var token = tokenProp.GetString()!;
            return await SetTokenAndFetchUserAsync(token, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AuthError = $"Token exchange error: {ex.Message}";
            _logError?.Invoke("CompleteAuthWithCodeAsync failed", ex);
            return false;
        }
    }

    public async Task<bool> LoginWithTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            AuthError = "Please enter a valid GitHub token.";
            return false;
        }

        IsBusy = true;
        AuthError = null;
        try
        {
            return await SetTokenAndFetchUserAsync(token.Trim(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> SetTokenAndFetchUserAsync(string token, CancellationToken cancellationToken)
    {
        var user = await FetchUserProfileAsync(token, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            AuthError = "Could not retrieve user profile from GitHub with this token. Check token permissions.";
            return false;
        }

        _accessToken = token;
        _currentUser = user;
        _secureStorage.SetSecret("GitHubAccessToken", token);

        try
        {
            var userJson = JsonSerializer.Serialize(user);
            _secureStorage.SetSecret("GitHubCachedUser", userJson);
        }
        catch { }

        _logInfo?.Invoke($"GitHub user signed in successfully: @{user.Login} ({user.Name})");
        AuthStateChanged?.Invoke(_currentUser);
        return true;
    }

    public async Task<GitHubUser?> FetchUserProfileAsync(string token, CancellationToken cancellationToken = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.UserAgent.ParseAdd("OrionStorePC/1.0");

            using var resp = await _httpClient.SendAsync(req, cancellationToken).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                _logInfo?.Invoke($"GitHub /user returned status {(int)resp.StatusCode}");
                return null;
            }

            var json = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var user = JsonSerializer.Deserialize<GitHubUser>(json, JsonOptions);
            return user;
        }
        catch (Exception ex)
        {
            _logError?.Invoke("Failed to fetch GitHub user profile", ex);
            return null;
        }
    }

    public void Logout()
    {
        _accessToken = null;
        _currentUser = null;
        _secureStorage.RemoveSecret("GitHubAccessToken");
        _secureStorage.RemoveSecret("GitHubCachedUser");
        _logInfo?.Invoke("GitHub user signed out.");
        AuthStateChanged?.Invoke(null);
    }
}
