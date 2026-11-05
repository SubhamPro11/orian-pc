using Orion.Core.Models;
using Orion.Core.Services;
using Xunit;

namespace Orion.Tests;

public class VersionServiceTests
{
    private readonly VersionService _service = new();

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2.3-beta1", "1.2.3")]
    [InlineData("v2.0.0+build123", "2.0.0")]
    [InlineData("1.0", "1.0")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void NormalizeVersion_CleansVersionCorrectly(string? input, string expected)
    {
        var result = _service.NormalizeVersion(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("1.2.3", "1.2.3", 0)]
    [InlineData("1.2.4", "1.2.3", 1)]
    [InlineData("1.2.3", "1.2.4", -1)]
    [InlineData("v2.0", "1.9.9", 1)]
    [InlineData("1.0.0.1", "1.0.0", 1)]
    [InlineData("1.10.0", "1.9.0", 1)]
    [InlineData("1.0.0", "1.0.0.0", 0)]
    public void CompareVersions_OrdersCorrectly(string v1, string v2, int expectedSign)
    {
        var cmp = _service.CompareVersions(v1, v2);
        Assert.Equal(expectedSign, Math.Sign(cmp));
    }

    [Theory]
    [InlineData("v1.2.3", "1.2.3", true)]
    [InlineData("1.2.3-beta", "1.2.3", true)]
    [InlineData("1.2.3", "1.2.4", false)]
    public void IsSameVersion_MatchesEquivalentVersions(string v1, string v2, bool expected)
    {
        var result = _service.IsSameVersion(v1, v2);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("FlowLauncher-v1.17.0.exe", "1.17.0")]
    [InlineData("app-release-2.6.5.apk", "2.6.5")]
    [InlineData("OrionStore_1.0.0_x64_setup.exe", "1.0.0")]
    [InlineData("v3.4.5.6", "3.4.5.6")]
    public void ExtractVersion_ExtractsCorrectly(string filename, string expected)
    {
        var result = _service.ExtractVersion(filename);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("app-nightly.exe", "v1.0", false, "Nightly")]
    [InlineData("app-alpha.zip", "v1.0", false, "Alpha")]
    [InlineData("app-beta.msi", "v1.0", false, "Beta")]
    [InlineData("app.exe", "v1.0-rc1", false, "Beta")]
    [InlineData("app.exe", "v1.0", true, "Beta")]
    [InlineData("app.exe", "v1.0", false, "Stable")]
    public void DetectStream_IdentifiesStreamCorrectly(string file, string tag, bool prerelease, string expected)
    {
        var stream = _service.DetectStream(file, tag, prerelease);
        Assert.Equal(expected, stream);
    }

    [Fact]
    public void HasAvailableUpdate_DetectsUpdate()
    {
        var app = new AppItem(
            Id: "test-app",
            Name: "Test App",
            LatestVersion: "1.5.0",
            AvailableVersions: new[]
            {
                new VersionOption("Stable", "1.5.0", "2026-01-01", Array.Empty<AppVariant>())
            }
        );

        Assert.True(_service.HasAvailableUpdate(app, "1.4.0"));
        Assert.False(_service.HasAvailableUpdate(app, "1.5.0"));
        Assert.False(_service.HasAvailableUpdate(app, "1.6.0"));
    }
}

