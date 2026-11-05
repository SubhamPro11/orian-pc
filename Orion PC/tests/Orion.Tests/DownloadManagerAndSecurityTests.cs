using Orion.Core.Models;
using Orion.Core.Services;
using Orion.Platform.Windows.Services;
using Xunit;

namespace Orion.Tests;

public class DownloadManagerAndSecurityTests
{
    private readonly DownloadManager _downloadManager;
    private readonly WindowsSecureStorage _secureStorage;

    public DownloadManagerAndSecurityTests()
    {
        var httpClient = new HttpClient();
        var hashVerifier = new HashVerifier();
        var assetResolver = new AssetResolver();
        _downloadManager = new DownloadManager(httpClient, hashVerifier, assetResolver);
        _secureStorage = new WindowsSecureStorage();
    }

    [Fact]
    public void Enqueue_ThrowsOnInsecureHttpUrl()
    {
        var app = new AppItem(Id: "win1", Name: "Insecure App", Platform: "PC");
        var variant = new AppVariant("X64", "http://insecure.example.com/app.exe", 1000, PackageType.Exe);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            _downloadManager.Enqueue(app, variant));

        Assert.Contains("Insecure download blocked", ex.Message);
    }

    [Fact]
    public void Enqueue_ThrowsOnNonWindowsPackageType()
    {
        var app = new AppItem(Id: "win1", Name: "Android Package App", Platform: "PC");
        var variant = new AppVariant("Universal", "https://secure.example.com/app.apk", 1000, PackageType.Apk);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            _downloadManager.Enqueue(app, variant));

        Assert.Contains("Refused non-Windows asset", ex.Message);
    }

    [Fact]
    public void CheckAuthenticode_DetectsMicrosoftSignatureOnDotnetBinary()
    {
        var dotnetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
        Assert.True(File.Exists(dotnetPath));

        var certInfo = _downloadManager.CheckAuthenticode(dotnetPath);

        Assert.True(certInfo.IsSigned);
        Assert.NotNull(certInfo.Subject);
        Assert.Contains("Microsoft", certInfo.Subject);
    }

    [Fact]
    public void CheckAuthenticode_FlagsUnsignedBinaryCorrectly()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"unsigned_{Guid.NewGuid()}.exe");
        try
        {
            File.WriteAllText(tempFile, "This is not a signed binary.");
            var certInfo = _downloadManager.CheckAuthenticode(tempFile);

            Assert.False(certInfo.IsSigned);
            Assert.Contains("unsigned", certInfo.StatusMessage.ToLowerInvariant());
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void SecureStorage_StoresAndRetrievesEncryptedSecrets()
    {
        const string key = "TestApiKey_12345";
        const string secretValue = "vt_live_secret_token_abcdef123456";

        try
        {
            _secureStorage.SetSecret(key, secretValue);
            var retrieved = _secureStorage.GetSecret(key);

            Assert.Equal(secretValue, retrieved);

            _secureStorage.RemoveSecret(key);
            var retrievedAfterRemoval = _secureStorage.GetSecret(key);
            Assert.Null(retrievedAfterRemoval);
        }
        finally
        {
            _secureStorage.RemoveSecret(key);
        }
    }
}
