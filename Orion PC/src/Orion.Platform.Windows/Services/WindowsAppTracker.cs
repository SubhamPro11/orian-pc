using System.Text.Json;
using Microsoft.Win32;
using Orion.Core.Models;
using Orion.Core.Services;

namespace Orion.Platform.Windows.Services;

public record InstalledAppInfo(
    string DisplayName,
    string? DisplayVersion,
    string? Publisher,
    string? InstallLocation,
    string? UninstallString,
    string? QuietUninstallString,
    string Source, // "Registry-HKLM", "Registry-HKCU", "MSIX", "Portable"
    string? PackageFamilyName = null
);

public interface IInstalledAppDetector
{
    IReadOnlyList<InstalledAppInfo> GetInstalledApps(bool forceRefresh = false);
    InstalledAppInfo? FindInstalledApp(AppItem app);
    bool IsAppInstalled(AppItem app);
    string? GetInstalledVersion(AppItem app);
    void RegisterPortableApp(string appId, string appName, string version, string installPath, string exePath);
    void UnregisterPortableApp(string appId);
}

public class WindowsAppTracker : IInstalledAppDetector
{
    private readonly IVersionService _versionService;
    private readonly string _portableDbPath;
    private List<InstalledAppInfo>? _cachedApps;
    private readonly object _lock = new();

    public WindowsAppTracker(IVersionService versionService)
    {
        _versionService = versionService;
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(localAppData, "OrionStore");
        Directory.CreateDirectory(dir);
        _portableDbPath = Path.Combine(dir, "installed_portable.json");
    }

    public IReadOnlyList<InstalledAppInfo> GetInstalledApps(bool forceRefresh = false)
    {
        lock (_lock)
        {
            if (_cachedApps != null && !forceRefresh)
            {
                return _cachedApps;
            }

            var apps = new List<InstalledAppInfo>();

            // 1. Scan Registry (HKLM & HKCU, 64-bit and WOW6432Node)
            ScanRegistryKey(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "Registry-HKLM", apps);
            ScanRegistryKey(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "Registry-HKCU", apps);
            ScanRegistryKey(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", "Registry-HKLM-32", apps);
            ScanRegistryKey(Registry.CurrentUser, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", "Registry-HKCU-32", apps);

            // 2. Scan MSIX packages via WinRT
            ScanMsixPackages(apps);

            // 3. Scan Orion's tracked portable installations
            ScanPortableApps(apps);

            _cachedApps = apps
                .GroupBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            return _cachedApps;
        }
    }

    private static void ScanRegistryKey(RegistryKey rootKey, string subKeyPath, string source, List<InstalledAppInfo> results)
    {
        try
        {
            using var key = rootKey.OpenSubKey(subKeyPath);
            if (key == null) return;

            foreach (var subKeyName in key.GetSubKeyNames())
            {
                try
                {
                    using var subKey = key.OpenSubKey(subKeyName);
                    if (subKey == null) continue;

                    var displayName = subKey.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(displayName)) continue;

                    var displayVersion = subKey.GetValue("DisplayVersion") as string;
                    var publisher = subKey.GetValue("Publisher") as string;
                    var installLocation = subKey.GetValue("InstallLocation") as string;
                    var uninstallString = subKey.GetValue("UninstallString") as string;
                    var quietUninstallString = subKey.GetValue("QuietUninstallString") as string;

                    var systemComponent = subKey.GetValue("SystemComponent");
                    if (systemComponent is int sys && sys == 1) continue;

                    results.Add(new InstalledAppInfo(
                        displayName.Trim(),
                        displayVersion?.Trim(),
                        publisher?.Trim(),
                        installLocation?.Trim(),
                        uninstallString?.Trim(),
                        quietUninstallString?.Trim(),
                        source
                    ));
                }
                catch
                {
                    // Ignore individual key read errors
                }
            }
        }
        catch
        {
            // Ignore registry branch access errors
        }
    }

    private static void ScanMsixPackages(List<InstalledAppInfo> results)
    {
        try
        {
            var pm = new global::Windows.Management.Deployment.PackageManager();
            var packages = pm.FindPackages();

            foreach (var package in packages)
            {
                try
                {
                    if (package.IsFramework || package.IsResourcePackage) continue;

                    var name = package.DisplayName;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        name = package.Id.Name;
                    }

                    var v = package.Id.Version;
                    var versionStr = $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";

                    results.Add(new InstalledAppInfo(
                        name,
                        versionStr,
                        package.PublisherDisplayName,
                        package.InstalledLocation?.Path,
                        null,
                        null,
                        "MSIX",
                        package.Id.FamilyName
                    ));
                }
                catch
                {
                    // Ignore individual package errors
                }
            }
        }
        catch
        {
            // WinRT PackageManager may be unavailable or restricted in some contexts
        }
    }

    private void ScanPortableApps(List<InstalledAppInfo> results)
    {
        try
        {
            if (!File.Exists(_portableDbPath)) return;

            var json = File.ReadAllText(_portableDbPath);
            var entries = JsonSerializer.Deserialize<Dictionary<string, PortableAppRecord>>(json);
            if (entries == null) return;

            foreach (var (id, entry) in entries)
            {
                if (Directory.Exists(entry.InstallPath) || File.Exists(entry.ExePath))
                {
                    results.Add(new InstalledAppInfo(
                        entry.Name,
                        entry.Version,
                        "Orion Portable",
                        entry.InstallPath,
                        entry.ExePath,
                        null,
                        "Portable"
                    ));
                }
            }
        }
        catch
        {
            // Ignore corrupted local portable db
        }
    }

    public InstalledAppInfo? FindInstalledApp(AppItem app)
    {
        var installed = GetInstalledApps();
        var targetName = app.Name.Trim().ToLowerInvariant();
        var targetId = app.Id.Trim().ToLowerInvariant();

        // Exact name match
        var match = installed.FirstOrDefault(i => i.DisplayName.Trim().Equals(targetName, StringComparison.OrdinalIgnoreCase));
        if (match != null) return match;

        // Substring / identifier match
        match = installed.FirstOrDefault(i =>
        {
            var dn = i.DisplayName.ToLowerInvariant();
            return dn.Contains(targetName) || dn.Contains(targetId);
        });

        return match;
    }

    public bool IsAppInstalled(AppItem app) => FindInstalledApp(app) != null;

    public string? GetInstalledVersion(AppItem app)
    {
        var match = FindInstalledApp(app);
        return match?.DisplayVersion;
    }

    public void RegisterPortableApp(string appId, string appName, string version, string installPath, string exePath)
    {
        lock (_lock)
        {
            Dictionary<string, PortableAppRecord> entries;
            try
            {
                entries = File.Exists(_portableDbPath)
                    ? JsonSerializer.Deserialize<Dictionary<string, PortableAppRecord>>(File.ReadAllText(_portableDbPath)) ?? new()
                    : new();
            }
            catch
            {
                entries = new();
            }

            entries[appId] = new PortableAppRecord(appId, appName, version, installPath, exePath, DateTime.UtcNow);
            File.WriteAllText(_portableDbPath, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
            _cachedApps = null; // Invalidate cache
        }
    }

    public void UnregisterPortableApp(string appId)
    {
        lock (_lock)
        {
            if (!File.Exists(_portableDbPath)) return;
            try
            {
                var entries = JsonSerializer.Deserialize<Dictionary<string, PortableAppRecord>>(File.ReadAllText(_portableDbPath)) ?? new();
                if (entries.Remove(appId))
                {
                    File.WriteAllText(_portableDbPath, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
                    _cachedApps = null;
                }
            }
            catch
            {
                // Ignore
            }
        }
    }

    public record PortableAppRecord(string Id, string Name, string Version, string InstallPath, string ExePath, DateTime InstalledAt);
}
