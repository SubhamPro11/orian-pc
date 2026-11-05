using Orion.Core.Models;
using Orion.Core.Services;
using Xunit;

namespace Orion.Tests;

public class AssetResolverTests
{
    private readonly AssetResolver _resolver = new();

    [Theory]
    [InlineData("FlowLauncher-v1.17.0-win-x64.exe", CpuArchitecture.X64)]
    [InlineData("PowerToysSetup-0.80.0-x64.exe", CpuArchitecture.X64)]
    [InlineData("PowerToysSetup-0.80.0-arm64.exe", CpuArchitecture.Arm64)]
    [InlineData("App-Setup-ia32.exe", CpuArchitecture.X86)]
    [InlineData("App-universal.exe", CpuArchitecture.Universal)]
    public void DetermineArchitecture_IdentifiesCorrectArch(string filename, CpuArchitecture expected)
    {
        var arch = _resolver.DetermineArchitecture(filename);
        Assert.Equal(expected, arch);
    }

    [Theory]
    [InlineData("setup.exe", PackageType.Exe)]
    [InlineData("setup.msi", PackageType.Msi)]
    [InlineData("app.msixbundle", PackageType.Msix)]
    [InlineData("app.msix", PackageType.Msix)]
    [InlineData("portable.zip", PackageType.ZipPortable)]
    [InlineData("app.apk", PackageType.Apk)]
    [InlineData("app.dmg", PackageType.Dmg)]
    [InlineData("app.AppImage", PackageType.AppImage)]
    [InlineData("app.deb", PackageType.Deb)]
    public void DeterminePackageType_IdentifiesCorrectType(string filename, PackageType expected)
    {
        var type = _resolver.DeterminePackageType(filename);
        Assert.Equal(expected, type);
    }

    [Theory]
    [InlineData(PackageType.Exe, true)]
    [InlineData(PackageType.Msi, true)]
    [InlineData(PackageType.Msix, true)]
    [InlineData(PackageType.ZipPortable, true)]
    [InlineData(PackageType.Apk, false)]
    [InlineData(PackageType.Dmg, false)]
    [InlineData(PackageType.AppImage, false)]
    [InlineData(PackageType.Deb, false)]
    [InlineData(PackageType.Unknown, false)]
    public void IsWindowsPackage_FiltersNonWindows(PackageType type, bool expected)
    {
        var isWin = _resolver.IsWindowsPackage(type);
        Assert.Equal(expected, isWin);
    }

    [Fact]
    public void SelectBestAsset_PrefersMatchingArchAndInstallerOverZip()
    {
        var variants = new[]
        {
            new AppVariant("X86", "http://dl/app-x86.zip", 1000, PackageType.ZipPortable),
            new AppVariant("X86", "http://dl/app-x86.exe", 1000, PackageType.Exe),
            new AppVariant("X64", "http://dl/app-x64.zip", 1200, PackageType.ZipPortable),
            new AppVariant("X64", "http://dl/app-x64.msi", 1200, PackageType.Msi),
            new AppVariant("Arm64", "http://dl/app-arm64.exe", 1100, PackageType.Exe)
        };

        var bestForX64 = _resolver.SelectBestAsset(variants, CpuArchitecture.X64);
        Assert.NotNull(bestForX64);
        Assert.Equal("X64", bestForX64.Arch);
        Assert.Equal(PackageType.Msi, bestForX64.PackageType);

        var bestForArm64 = _resolver.SelectBestAsset(variants, CpuArchitecture.Arm64);
        Assert.NotNull(bestForArm64);
        Assert.Equal("Arm64", bestForArm64.Arch);
        Assert.Equal(PackageType.Exe, bestForArm64.PackageType);
    }

    [Fact]
    public void SelectBestAsset_IgnoresNonWindowsPackagesWhenWindowsExists()
    {
        var variants = new[]
        {
            new AppVariant("Universal", "http://dl/app.apk", 1000, PackageType.Apk),
            new AppVariant("X64", "http://dl/app.dmg", 1200, PackageType.Dmg),
            new AppVariant("X64", "http://dl/app-setup.exe", 1100, PackageType.Exe)
        };

        var best = _resolver.SelectBestAsset(variants, CpuArchitecture.X64);
        Assert.NotNull(best);
        Assert.Equal(PackageType.Exe, best.PackageType);
    }
}

