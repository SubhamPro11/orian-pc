using System.IO.Compression;
using System.Text;
using Orion.Core.Models;
using Orion.Core.Services;
using Orion.Platform.Windows.Services;
using Xunit;

namespace Orion.Tests;

public class WindowsInstallerServiceTests
{
    private readonly WindowsInstallerService _installer;
    private readonly WindowsAppTracker _tracker;

    public WindowsInstallerServiceTests()
    {
        var versionService = new VersionService();
        var assetResolver = new AssetResolver();
        _tracker = new WindowsAppTracker(versionService);
        _installer = new WindowsInstallerService(assetResolver, _tracker);
    }

    [Fact]
    public void ValidateAssetForWindows_ThrowsOnNonWindowsApp()
    {
        var androidApp = new AppItem(
            Id: "com.example.android",
            Name: "Android App",
            Platform: "Android"
        );

        var variant = new AppVariant("Universal", "https://example.com/app.apk", 1000, PackageType.Apk);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            _installer.ValidateAssetForWindows(androidApp, variant));

        Assert.Contains("Refused non-Windows app", ex.Message);
    }

    [Fact]
    public void ValidateAssetForWindows_ThrowsOnNonWindowsPackageType()
    {
        var winApp = new AppItem(
            Id: "win.app",
            Name: "Windows App",
            Platform: "PC"
        );

        var dmgVariant = new AppVariant("X64", "https://example.com/app.dmg", 1000, PackageType.Dmg);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            _installer.ValidateAssetForWindows(winApp, dmgVariant));

        Assert.Contains("Refused non-Windows asset", ex.Message);
    }

    [Fact]
    public async Task InstallPortableZip_RejectsZipSlipPathTraversal()
    {
        var tempZipPath = Path.Combine(Path.GetTempPath(), $"zipslip_test_{Guid.NewGuid()}.zip");
        try
        {
            // Create a malicious zip file containing path traversal
            using (var zipStream = new FileStream(tempZipPath, FileMode.Create))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                var maliciousEntry = archive.CreateEntry("../evil_path_traversal.txt");
                using var writer = new StreamWriter(maliciousEntry.Open());
                writer.WriteLine("malicious content");
            }

            var app = new AppItem(Id: "zip-slip-app", Name: "Malicious App", Platform: "PC");
            var variant = new AppVariant("X64", "https://example.com/app.zip", 100, PackageType.ZipPortable);

            var result = await _installer.InstallAsync(app, variant, tempZipPath);

            Assert.False(result.Success);
            Assert.Contains("Zip-Slip", result.Message);
        }
        finally
        {
            if (File.Exists(tempZipPath)) File.Delete(tempZipPath);
        }
    }

    [Fact]
    public async Task InstallPortableZip_ExtractsAndRegistersSafeApp()
    {
        var tempZipPath = Path.Combine(Path.GetTempPath(), $"safe_zip_test_{Guid.NewGuid()}.zip");
        var appId = $"test_safe_app_{Guid.NewGuid():N}";

        try
        {
            // Create a valid, safe zip file
            using (var zipStream = new FileStream(tempZipPath, FileMode.Create))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                var exeEntry = archive.CreateEntry("app.exe");
                using (var writer = new StreamWriter(exeEntry.Open()))
                {
                    writer.WriteLine("dummy executable binary");
                }

                var textEntry = archive.CreateEntry("readme.txt");
                using (var writer = new StreamWriter(textEntry.Open()))
                {
                    writer.WriteLine("Orion test readme");
                }
            }

            var app = new AppItem(Id: appId, Name: "Safe Portable App", Version: "1.0.0", Platform: "PC");
            var variant = new AppVariant("X64", "https://example.com/app.zip", 100, PackageType.ZipPortable);

            var result = await _installer.InstallAsync(app, variant, tempZipPath);

            Assert.True(result.Success);
            Assert.NotNull(result.InstalledLocation);
            Assert.True(Directory.Exists(result.InstalledLocation));
            Assert.True(File.Exists(Path.Combine(result.InstalledLocation, "app.exe")));

            // Verify tracker registers the installed portable app
            var found = _tracker.FindInstalledApp(app);
            Assert.NotNull(found);
            Assert.Equal("1.0.0", found.DisplayVersion);
            Assert.Equal("Portable", found.Source);

            // Clean up uninstall
            var uninstalled = await _installer.UninstallAsync(found);
            Assert.True(uninstalled);
            Assert.False(Directory.Exists(result.InstalledLocation));
        }
        finally
        {
            if (File.Exists(tempZipPath)) File.Delete(tempZipPath);
        }
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../evil")]
    [InlineData("CON")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("LPT1")]
    [InlineData("trailing.")]
    [InlineData("invalid/path/traversal")]
    public void TrySanitizeId_RejectsMaliciousAndReservedIds(string maliciousId)
    {
        bool isValid = WindowsInstallerService.TrySanitizeId(maliciousId, out var sanitized);
        Assert.False(isValid);
        Assert.Empty(sanitized);
    }

    [Fact]
    public void TrySanitizeId_AcceptsValidAppIds()
    {
        bool isValid = WindowsInstallerService.TrySanitizeId("flow-launcher_1.17", out var sanitized);
        Assert.True(isValid);
        Assert.Equal("flow-launcher_1.17", sanitized);
    }

    [Theory]
    [InlineData("MsiExec.exe /X{12345678-ABCD-1234-ABCD-123456789ABC} /qn", "msiexec.exe", "/x{12345678-ABCD-1234-ABCD-123456789ABC} /qn")]
    [InlineData("\"C:\\Program Files\\App\\uninstall.exe\" /silent", "C:\\Program Files\\App\\uninstall.exe", "/silent")]
    [InlineData("C:\\Tools\\unins000.exe", "C:\\Tools\\unins000.exe", "")]
    public void TryParseUninstallCommand_ParsesCommandsSafelyWithoutCmd(string rawCommand, string expectedExe, string expectedArgs)
    {
        bool success = WindowsInstallerService.TryParseUninstallCommand(rawCommand, out var exe, out var args);
        Assert.True(success);
        Assert.Equal(expectedExe.ToLowerInvariant(), exe.ToLowerInvariant());
        Assert.Equal(expectedArgs.ToLowerInvariant(), args.ToLowerInvariant());
    }

    [Fact]
    public async Task InstallPortableZip_RejectsZipBombHighCompressionRatio()
    {
        var tempZipPath = Path.Combine(Path.GetTempPath(), $"zipbomb_test_{Guid.NewGuid()}.zip");
        try
        {
            using (var zipStream = new FileStream(tempZipPath, FileMode.Create))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("huge_sparse_file.bin", CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                // Write repeating zeroes which compress down to tiny size (huge ratio > 100x)
                var zeroBlock = new byte[65536];
                for (int i = 0; i < 300; i++) // ~19.6 MB of zeros, compresses to ~20 KB (> 900x ratio)
                {
                    entryStream.Write(zeroBlock, 0, zeroBlock.Length);
                }
            }

            var app = new AppItem(Id: $"bomb_app_{Guid.NewGuid():N}", Name: "ZipBomb App", Platform: "PC");
            var variant = new AppVariant("X64", "https://example.com/bomb.zip", 100, PackageType.ZipPortable);

            var result = await _installer.InstallAsync(app, variant, tempZipPath);

            Assert.False(result.Success);
            Assert.Contains("zip-bomb", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(tempZipPath)) File.Delete(tempZipPath);
        }
    }
}

