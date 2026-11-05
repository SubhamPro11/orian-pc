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
    string? PackageFamilyName = null,
    string? PackageFullName = null,
    string? AppId = null
);

public interface IInstalledAppDetector
{
    IReadOnlyList<InstalledAppInfo> GetInstalledApps(bool forceRefresh = false);
    Task<IReadOnlyList<InstalledAppInfo>> GetInstalledAppsAsync(bool forceRefresh = false);
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

            _cachedApps = PerformScan();
            return _cachedApps;
        }
    }

    public Task<IReadOnlyList<InstalledAppInfo>> GetInstalledAppsAsync(bool forceRefresh = false)
    {
        return Task.Run(() => GetInstalledApps(forceRefresh));
    }

    private List<InstalledAppInfo> PerformScan()
    {
        var apps = new List<InstalledAppInfo>();

        // 1. Scan Registry (HKLM & HKCU, 64-bit and WOW6432Node)
        ScanRegistryKey(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "Registry-HKLM", apps);
        ScanRegistryKey(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "Registry-HKCU", apps);
        ScanRegistryKey(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", "Registry-HKLM-32", apps);
        ScanRegistryKey(Registry.CurrentUser, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", "Registry-HKCU-32", apps);

        // 2. Scan MSIX packages for current user via WinRT
        ScanMsixPackages(apps);

        // 3. Scan Orion's tracked portable installations
        ScanPortableApps(apps);

        return apps
            .GroupBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
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
            // Use FindPackagesForUser with string.Empty for current user to avoid requiring admin privileges
            var packages = pm.FindPackagesForUser(string.Empty);

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
                        PackageFamilyName: package.Id.FamilyName,
                        PackageFullName: package.Id.FullName
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
            // WinRT PackageManager may be unavailable or restricted in some test environments
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
                        "Portable",
                        AppId: id
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
        if (app == null) return null;

        var targetName = (app.Name ?? string.Empty).Trim();
        var targetId = (app.Id ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(targetName) && string.IsNullOrWhiteSpace(targetId))
        {
            return null;
        }

        var installed = GetInstalledApps();

        // 1. Exact name match
        if (!string.IsNullOrEmpty(targetName))
        {
            var match = installed.FirstOrDefault(i => i.DisplayName.Equals(targetName, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }

        // 2. Portable AppId match
        if (!string.IsNullOrEmpty(targetId))
        {
            var match = installed.FirstOrDefault(i => i.AppId != null && i.AppId.Equals(targetId, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }

        // 3. Substring match with minimum length threshold to prevent false matches
        if (targetName.Length >= 4)
        {
            var match = installed.FirstOrDefault(i => i.DisplayName.Contains(targetName, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }

        if (targetId.Length >= 4)
        {
            var match = installed.FirstOrDefault(i => i.DisplayName.Contains(targetId, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }

        return null;
    }

    public bool IsAppInstalled(AppItem app) => FindInstalledApp(app) != null;

    public string? GetInstalledVersion(AppItem app)
    {
        var match = FindInstalledApp(app);
        return match?.DisplayVersion;
    }

    private static readonly object ProcessLock = new();

    public void RegisterPortableApp(string appId, string appName, string version, string installPath, string exePath)
    {
        for (int retry = 0; retry < 5; retry++)
        {
            try
            {
                lock (ProcessLock)
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
                    return;
                }
            }
            catch (IOException) when (retry < 4)
            {
                Thread.Sleep(50 * (retry + 1));
            }
        }
    }

    public void UnregisterPortableApp(string appId)
    {
        for (int retry = 0; retry < 5; retry++)
        {
            try
            {
                lock (ProcessLock)
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
                        return;
                    }
                    catch
                    {
                        return;
                    }
                }
            }
            catch (IOException) when (retry < 4)
            {
                Thread.Sleep(50 * (retry + 1));
            }
        }
    }

    public record PortableAppRecord(string Id, string Name, string Version, string InstallPath, string ExePath, DateTime InstalledAt);
}
