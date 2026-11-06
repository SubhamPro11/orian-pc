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
    /// Preserves multi-platform support (e.g. "Android, Linux, MacOS, Windows").
    /// </summary>
    public static bool MatchesCategory(string? platform, IReadOnlyList<string>? tags, PlatformCategory category)
    {
        if (category == PlatformCategory.All)
        {
            return true;
        }

        var p = (platform ?? string.Empty).ToLowerInvariant();
        var t = tags ?? Array.Empty<string>();

        return category switch
        {
            PlatformCategory.Windows =>
                p.Contains("windows") || p.Contains("pc") || p.Contains("desktop") || p.Contains("win32") ||
                t.Any(tag => WindowsTags.Contains(tag)),

            PlatformCategory.Android =>
                (p.Contains("android") || p.Contains("tv") || p.Contains("apk") ||
                 t.Any(tag => AndroidTags.Contains(tag)) ||
                 (!p.Contains("windows") && !p.Contains("pc") && !p.Contains("linux") && !p.Contains("macos") && !p.Contains("mac")))
                && !(t.Any(tag => WindowsTags.Contains(tag)) && !t.Any(tag => AndroidTags.Contains(tag)) && !p.Contains(",")),

            PlatformCategory.Linux =>
                p.Contains("linux") || t.Any(tag => LinuxTags.Contains(tag)),

            PlatformCategory.MacOS =>
                p.Contains("macos") || p.Contains("mac") || p.Contains("darwin") || t.Any(tag => MacTags.Contains(tag)),

            _ => true
        };
    }
}

