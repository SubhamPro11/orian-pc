using Orion.Core.Models;

namespace Orion.Core.Services;

public interface IAssetResolver
{
    CpuArchitecture DetermineArchitecture(string name);
    PackageType DeterminePackageType(string name, string? url = null);
    bool IsWindowsPackage(PackageType packageType);
    bool IsArchitectureCompatible(CpuArchitecture variantArch, CpuArchitecture osArch, out bool isRecommended);
    AppVariant? SelectBestAsset(IEnumerable<AppVariant> variants, CpuArchitecture osArch);
}

public class AssetResolver : IAssetResolver
{
    public CpuArchitecture DetermineArchitecture(string name)
    {
        var l = name.ToLowerInvariant();
        if (l.Contains("arm64") || l.Contains("aarch64") || l.Contains("v8a"))
        {
            return CpuArchitecture.Arm64;
        }

        if (l.Contains("x86_64") || l.Contains("x64") || l.Contains("amd64") || l.Contains("win64"))
        {
            return CpuArchitecture.X64;
        }

        if (l.Contains("x86") || l.Contains("i386") || l.Contains("i686") || l.Contains("win32") || l.Contains("ia32"))
        {
            return CpuArchitecture.X86;
        }

        if (l.Contains("universal") || l.Contains("-all") || l.Contains("_all") || l.Contains("any"))
        {
            return CpuArchitecture.Universal;
        }

        return CpuArchitecture.Universal;
    }

    public PackageType DeterminePackageType(string name, string? url = null)
    {
        var combined = $"{name} {url}".ToLowerInvariant();

        if (combined.Contains(".msixbundle") || combined.Contains(".msix") || combined.Contains(".appxbundle") || combined.Contains(".appx"))
        {
            return PackageType.Msix;
        }

        if (combined.Contains(".msi"))
        {
            return PackageType.Msi;
        }

        if (combined.Contains(".exe"))
        {
            return PackageType.Exe;
        }

        if (combined.Contains(".zip") || combined.Contains(".7z"))
        {
            return PackageType.ZipPortable;
        }

        if (combined.Contains(".apk") || combined.Contains(".xapk") || combined.Contains(".apks"))
        {
            return PackageType.Apk;
        }

        if (combined.Contains(".dmg") || combined.Contains(".pkg"))
        {
            return PackageType.Dmg;
        }

        if (combined.Contains(".appimage"))
        {
            return PackageType.AppImage;
        }

        if (combined.Contains(".deb"))
        {
            return PackageType.Deb;
        }

        if (combined.Contains(".rpm"))
        {
            return PackageType.Rpm;
        }

        return PackageType.Unknown;
    }

    public bool IsWindowsPackage(PackageType packageType)
    {
        return packageType is PackageType.Exe
            or PackageType.Msi
            or PackageType.Msix
            or PackageType.ZipPortable;
    }

    public bool IsArchitectureCompatible(CpuArchitecture variantArch, CpuArchitecture osArch, out bool isRecommended)
    {
        if (variantArch == osArch && osArch != CpuArchitecture.Unknown)
        {
            isRecommended = true;
            return true;
        }

        if (variantArch == CpuArchitecture.Universal)
        {
            isRecommended = false;
            return true;
        }

        // On 64-bit Windows (x64), 32-bit x86 apps run via WOW64
        if (osArch == CpuArchitecture.X64 && variantArch == CpuArchitecture.X86)
        {
            isRecommended = false;
            return true;
        }

        // On Windows on ARM (Arm64), x64 and x86 run via emulation (Windows 11)
        if (osArch == CpuArchitecture.Arm64)
        {
            if (variantArch == CpuArchitecture.X64 || variantArch == CpuArchitecture.X86)
            {
                isRecommended = false;
                return true;
            }
        }

        isRecommended = false;
        return false;
    }

    public AppVariant? SelectBestAsset(IEnumerable<AppVariant> variants, CpuArchitecture osArch)
    {
        var list = variants.ToList();
        if (list.Count == 0) return null;

        // Filter only Windows packages if any are present
        var windowsVariants = list.Where(v => IsWindowsPackage(v.PackageType)).ToList();
        var candidates = windowsVariants.Count > 0 ? windowsVariants : list;

        // Rank candidates
        return candidates
            .Select(v => new
            {
                Variant = v,
                Compatible = IsArchitectureCompatible(v.PackageType == PackageType.Unknown ? DetermineArchitecture(v.Arch) : DetermineArchitecture(v.Url), osArch, out var rec),
                Recommended = rec,
                PackagePriority = GetPackageTypePriority(v.PackageType)
            })
            .Where(x => x.Compatible)
            .OrderByDescending(x => x.Recommended)
            .ThenByDescending(x => x.PackagePriority)
            .Select(x => x.Variant)
            .FirstOrDefault() ?? candidates.FirstOrDefault();
    }

    private static int GetPackageTypePriority(PackageType type)
    {
        return type switch
        {
            PackageType.Msix => 4,
            PackageType.Msi => 3,
            PackageType.Exe => 2,
            PackageType.ZipPortable => 1,
            _ => 0
        };
    }
}

