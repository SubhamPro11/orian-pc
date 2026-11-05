namespace Orion.Core.Models;

public static class PlatformConstants
{
    public const string Android = "Android";
    public const string PC = "PC";
    public const string TV = "TV";
    public const string Windows = "Windows";
    public const string Linux = "Linux";
    public const string MacOS = "macOS";

    public static readonly HashSet<string> WindowsTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "windows", "pc", "desktop", "win32", "win64", "win-x64", "win-arm64", "win"
    };

    public static readonly HashSet<string> AndroidTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "android", "apk", "mobile", "arm64-v8a", "armeabi-v7a"
    };

    public static readonly HashSet<string> LinuxTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "linux", "appimage", "flatpak", "snap", "deb", "rpm"
    };

    public static readonly HashSet<string> MacTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "macos", "mac", "darwin", "dmg", "pkg"
    };

    /// <summary>
    /// Normalizes a catalog platform string to one of the standard Orion platforms ("PC", "Android", "TV").
    /// </summary>
    public static string NormalizePlatform(string? rawPlatform, IEnumerable<string>? tags = null)
    {
        var p = rawPlatform?.Trim();
        if (!string.IsNullOrEmpty(p))
        {
            if (p.Equals("pc", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("desktop", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("windows", StringComparison.OrdinalIgnoreCase))
            {
                return PC;
            }

            if (p.Equals("tv", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("androidtv", StringComparison.OrdinalIgnoreCase))
            {
                return TV;
            }

            if (p.Equals("linux", StringComparison.OrdinalIgnoreCase))
            {
                return Linux;
            }

            if (p.Equals("macos", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("mac", StringComparison.OrdinalIgnoreCase))
            {
                return MacOS;
            }

            if (p.Contains("windows", StringComparison.OrdinalIgnoreCase) ||
                p.Contains("pc", StringComparison.OrdinalIgnoreCase))
            {
                return PC;
            }
        }

        if (tags != null)
        {
            foreach (var tag in tags)
            {
                if (WindowsTags.Contains(tag))
                {
                    return PC;
                }
            }
        }

        return Android;
    }

    /// <summary>
    /// Matches an app's platform and tags to the selected device filter category.
    /// </summary>
    public static bool MatchesCategory(string platform, IReadOnlyList<string>? tags, PlatformCategory category)
    {
        if (category == PlatformCategory.All)
        {
            return true;
        }

        var normalized = NormalizePlatform(platform, tags);

        return category switch
        {
            PlatformCategory.Windows => normalized == PC,
            PlatformCategory.Android => normalized == Android || normalized == TV,
            PlatformCategory.Linux => normalized == Linux || (tags != null && tags.Any(t => LinuxTags.Contains(t))),
            PlatformCategory.MacOS => normalized == MacOS || (tags != null && tags.Any(t => MacTags.Contains(t))),
            _ => true
        };
    }
}

