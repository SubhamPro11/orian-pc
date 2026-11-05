using System.Text.RegularExpressions;
using Orion.Core.Models;

namespace Orion.Core.Services;

public interface IVersionService
{
    string NormalizeVersion(string? v);
    bool IsComparableVersion(string? v);
    bool IsSameVersion(string? a, string? b);
    int CompareVersions(string? a, string? b);
    int CompareSemver(string? a, string? b);
    string? ExtractVersion(string? s);
    string DetectStream(string filename, string tag, bool isPrerelease);
    string PreferredVersion(AppItem app, string stream = "Stable");
    bool HasAvailableUpdate(AppItem app, string? installedVersion, string stream = "Stable");
}

public class VersionService : IVersionService
{
    private static readonly Regex VersionRegex = new(@"v?(\d+(?:\.\d+)+)", RegexOptions.Compiled);
    private static readonly Regex SemverRegex = new(@"(\d+)\.(\d+)\.(\d+)", RegexOptions.Compiled);
    private static readonly Regex NonNumericDotRegex = new(@"[^0-9.]", RegexOptions.Compiled);

    private static readonly string[] VersionNoise =
    {
        "armeabi-v7a", "arm64-v8a", "x86_64", "x86", "v7a", "v8a",
        "win-x64", "win-arm64", "win32", "win64", "x64", "arm64",
        "-all", "_all", "-universal", "_universal", "universal",
        ".apk", ".exe", ".msi", ".zip", ".msixbundle", ".msix",
        "setup", "installer", "portable",
        "release", "mod", "beta", "alpha", "nightly", "debug"
    };

    public string NormalizeVersion(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return string.Empty;
        var s = v.Trim().ToLowerInvariant();
        if (s.StartsWith('v')) s = s[1..];
        var dash = s.IndexOf('-');
        if (dash >= 0) s = s[..dash];
        var plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];
        return NonNumericDotRegex.Replace(s, "").Trim();
    }

    public bool IsComparableVersion(string? v) => !string.IsNullOrEmpty(NormalizeVersion(v));

    public bool IsSameVersion(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        var na = NormalizeVersion(a);
        var nb = NormalizeVersion(b);
        return na.Length > 0 && na == nb;
    }

    public int CompareVersions(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0;
        var x = NormalizeVersion(a);
        var y = NormalizeVersion(b);
        if (x.Length == 0 || y.Length == 0 || x == y) return 0;

        var p = x.Split('.');
        var q = y.Split('.');
        var maxLen = Math.Max(p.Length, q.Length);

        for (int i = 0; i < maxLen; i++)
        {
            long m = i < p.Length && long.TryParse(p[i], out var v1) ? v1 : 0;
            long n = i < q.Length && long.TryParse(q[i], out var v2) ? v2 : 0;
            if (m != n) return m.CompareTo(n);
        }

        return 0;
    }

    public int CompareSemver(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b)) return 0;
        if (string.IsNullOrEmpty(a)) return -1;
        if (string.IsNullOrEmpty(b)) return 1;

        var m1 = SemverRegex.Match(a);
        var m2 = SemverRegex.Match(b);
        if (m1.Success && m2.Success)
        {
            for (int i = 1; i <= 3; i++)
            {
                int v1 = int.Parse(m1.Groups[i].Value);
                int v2 = int.Parse(m2.Groups[i].Value);
                if (v1 != v2) return v1.CompareTo(v2);
            }
            return 0;
        }

        return NaturalCompare(a, b);
    }

    private static int NaturalCompare(string a, string b)
    {
        var tokensA = Regex.Matches(a, @"\d+|\D+").Select(m => m.Value).ToList();
        var tokensB = Regex.Matches(b, @"\d+|\D+").Select(m => m.Value).ToList();
        var min = Math.Min(tokensA.Count, tokensB.Count);

        for (int i = 0; i < min; i++)
        {
            var p = tokensA[i];
            var q = tokensB[i];
            if (char.IsDigit(p[0]) && char.IsDigit(q[0]))
            {
                if (long.TryParse(p, out var numA) && long.TryParse(q, out var numB))
                {
                    int c = numA.CompareTo(numB);
                    if (c != 0) return c;
                }
            }
            else
            {
                int c = string.Compare(p, q, StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;
            }
        }

        return tokensA.Count.CompareTo(tokensB.Count);
    }

    public string? ExtractVersion(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var c = s.Trim().ToLowerInvariant();
        foreach (var noise in VersionNoise)
        {
            c = c.Replace(noise, "");
        }
        var m = VersionRegex.Match(c);
        return m.Success ? m.Groups[1].Value.Replace('-', '.') : null;
    }

    public string DetectStream(string filename, string tag, bool isPrerelease)
    {
        var t = $"{filename} {tag}".ToLowerInvariant();
        if (t.Contains("nightly") || t.Contains("daily") || t.Contains("snapshot")) return "Nightly";
        if (t.Contains("alpha") || t.Contains("canary") || t.Contains("debug")) return "Alpha";
        if (t.Contains("beta") || t.Contains("preview") || t.Contains("rc") || t.Contains("pre") || isPrerelease) return "Beta";
        return "Stable";
    }

    public string PreferredVersion(AppItem app, string stream = "Stable")
    {
        if (app.AvailableVersionsList.Count == 0) return app.LatestVersion;
        var match = app.AvailableVersionsList.FirstOrDefault(v => v.Type.Equals(stream, StringComparison.OrdinalIgnoreCase));
        if (match != null) return match.Version;
        if (!stream.Equals("Stable", StringComparison.OrdinalIgnoreCase))
        {
            var stable = app.AvailableVersionsList.FirstOrDefault(v => v.Type.Equals("Stable", StringComparison.OrdinalIgnoreCase));
            if (stable != null) return stable.Version;
        }
        return app.AvailableVersionsList[0].Version;
    }

    public bool HasAvailableUpdate(AppItem app, string? installedVersion, string stream = "Stable")
    {
        if (string.IsNullOrEmpty(installedVersion)) return false;
        var offer = PreferredVersion(app, stream);
        if (IsSameVersion(offer, installedVersion)) return false;

        return CompareVersions(offer, installedVersion) > 0;
    }
}

