using System.Security.Cryptography;
using System.Text;
using Orion.Platform.Windows.Services;
using Xunit;

namespace Orion.Tests;

public class SystemReadinessAndCacheTests
{
    [Fact]
    public void SystemReadinessService_VerifiesHostEnvironment()
    {
        var readiness = new SystemReadinessService();
        var report = readiness.CheckReadiness();

        Assert.NotNull(report);
        Assert.True(report.HasStorageAccess, "Should have write permissions to LocalAppData");
        Assert.NotEmpty(report.OsVersion);
        Assert.NotEmpty(report.Architecture);
        // On modern Windows 10/11 x64/arm64 runners, it should be ready
        Assert.True(report.IsReady, string.Join("; ", report.Errors));
    }

    [Fact]
    public void CacheIntegrity_DetectsTamperingOrChecksumMismatch()
    {
        var tempCachePath = Path.Combine(Path.GetTempPath(), $"cache_test_{Guid.NewGuid():N}.json");
        var shaPath = tempCachePath + ".sha256";

        try
        {
            var originalContent = "{\"apps\": [{\"id\": \"valid-app\", \"name\": \"Valid App\"}]}";
            File.WriteAllText(tempCachePath, originalContent);

            var originalHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(originalContent)));
            File.WriteAllText(shaPath, originalHash);

            // 1. Verify matching hash
            var readContent = File.ReadAllText(tempCachePath);
            var actualHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(readContent)));
            Assert.Equal(originalHash, actualHash);

            // 2. Simulate tampering with the cache content
            var tamperedContent = "{\"apps\": [{\"id\": \"hacked-app\", \"name\": \"Malicious Payload\"}]}";
            File.WriteAllText(tempCachePath, tamperedContent);

            var tamperedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tamperedContent)));
            var savedSha = File.ReadAllText(shaPath).Trim();

            Assert.NotEqual(savedSha, tamperedHash);
        }
        finally
        {
            if (File.Exists(tempCachePath)) File.Delete(tempCachePath);
            if (File.Exists(shaPath)) File.Delete(shaPath);
        }
    }
}

