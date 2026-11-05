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
        // Extract filename from URL AbsolutePath if valid, else from name
        string candidate = "";
        if (!string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            candidate = Path.GetFileName(uri.AbsolutePath);
        }

        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = Path.GetFileName(name ?? string.Empty);
        }

        var lower = candidate.ToLowerInvariant();

        if (lower.EndsWith(".msixbundle") || lower.EndsWith(".msix") || lower.EndsWith(".appxbundle") || lower.EndsWith(".appx"))
        {
            return PackageType.Msix;
        }

        if (lower.EndsWith(".msi"))
        {
            return PackageType.Msi;
        }

        if (lower.EndsWith(".exe"))
        {
            return PackageType.Exe;
        }

        if (lower.EndsWith(".zip") || lower.EndsWith(".7z"))
        {
            return PackageType.ZipPortable;
        }

        if (lower.EndsWith(".apk") || lower.EndsWith(".xapk") || lower.EndsWith(".apks"))
        {
            return PackageType.Apk;
        }

        if (lower.EndsWith(".dmg") || lower.EndsWith(".pkg"))
        {
            return PackageType.Dmg;
        }

        if (lower.EndsWith(".appimage"))
        {
            return PackageType.AppImage;
        }

        if (lower.EndsWith(".deb"))
        {
            return PackageType.Deb;
        }

        if (lower.EndsWith(".rpm"))
        {
            return PackageType.Rpm;
        }

        // If URL path didn't have an extension, try name's extension
        if (!string.IsNullOrWhiteSpace(name))
        {
            var nameFile = Path.GetFileName(name).ToLowerInvariant();
            if (nameFile.EndsWith(".msixbundle") || nameFile.EndsWith(".msix") || nameFile.EndsWith(".appxbundle") || nameFile.EndsWith(".appx")) return PackageType.Msix;
            if (nameFile.EndsWith(".msi")) return PackageType.Msi;
            if (nameFile.EndsWith(".exe")) return PackageType.Exe;
            if (nameFile.EndsWith(".zip") || nameFile.EndsWith(".7z")) return PackageType.ZipPortable;
            if (nameFile.EndsWith(".apk") || nameFile.EndsWith(".xapk") || nameFile.EndsWith(".apks")) return PackageType.Apk;
            if (nameFile.EndsWith(".dmg") || nameFile.EndsWith(".pkg")) return PackageType.Dmg;
            if (nameFile.EndsWith(".appimage")) return PackageType.AppImage;
            if (nameFile.EndsWith(".deb")) return PackageType.Deb;
            if (nameFile.EndsWith(".rpm")) return PackageType.Rpm;
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

