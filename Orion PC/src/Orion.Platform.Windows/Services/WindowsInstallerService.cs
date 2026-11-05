using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Orion.Core.Models;
using Orion.Core.Services;

namespace Orion.Platform.Windows.Services;

public record InstallResult(
    bool Success,
    string? Message = null,
    int? ProcessId = null,
    string? InstalledLocation = null,
    bool RebootRequired = false
);

public interface IWindowsInstallerService
{
    void ValidateAssetForWindows(AppItem app, AppVariant variant);
    Task<InstallResult> InstallAsync(AppItem app, AppVariant variant, string installerFilePath, bool silent = false, CancellationToken cancellationToken = default);
    Task<bool> UninstallAsync(InstalledAppInfo appInfo, CancellationToken cancellationToken = default);
}

public class WindowsInstallerService : IWindowsInstallerService
{
    private readonly IAssetResolver _assetResolver;
    private readonly IInstalledAppDetector _appTracker;
    private readonly string _appsRootDirectory;

    private const int MaxZipEntries = 10000;
    private const long MaxUncompressedZipBytes = 2L * 1024 * 1024 * 1024; // 2 GB
    private const double MaxZipCompressionRatio = 100.0;

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public WindowsInstallerService(IAssetResolver assetResolver, IInstalledAppDetector appTracker)
    {
        _assetResolver = assetResolver;
        _appTracker = appTracker;
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _appsRootDirectory = Path.Combine(localAppData, "OrionStore", "Apps");
        Directory.CreateDirectory(_appsRootDirectory);
    }

    public void ValidateAssetForWindows(AppItem app, AppVariant variant)
    {
        if (app == null) throw new ArgumentNullException(nameof(app));
        if (variant == null) throw new ArgumentNullException(nameof(variant));

        // 1. App platform check
        if (!app.IsWindowsApp)
        {
            throw new InvalidOperationException($"Refused non-Windows app: '{app.Name}' is an {app.Platform} application and cannot be installed on Windows.");
        }

        // 2. Package type check
        var packageType = variant.PackageType != PackageType.Unknown
            ? variant.PackageType
            : _assetResolver.DeterminePackageType(variant.Url);

        if (!_assetResolver.IsWindowsPackage(packageType))
        {
            throw new InvalidOperationException($"Refused non-Windows asset: '{variant.Url}' ({packageType}) cannot be installed on Windows.");
        }
    }

    public async Task<InstallResult> InstallAsync(
        AppItem app,
        AppVariant variant,
        string installerFilePath,
        bool silent = false,
        CancellationToken cancellationToken = default)
    {
        ValidateAssetForWindows(app, variant);

        if (!File.Exists(installerFilePath))
        {
            return new InstallResult(false, $"Installer file not found at: {installerFilePath}");
        }

        var packageType = variant.PackageType != PackageType.Unknown
            ? variant.PackageType
            : _assetResolver.DeterminePackageType(installerFilePath, variant.Url);

        return packageType switch
        {
            PackageType.Exe => await InstallExeAsync(installerFilePath, silent, cancellationToken),
            PackageType.Msi => await InstallMsiAsync(installerFilePath, silent, cancellationToken),
            PackageType.Msix => await InstallMsixAsync(installerFilePath, cancellationToken),
            PackageType.ZipPortable => await InstallPortableZipAsync(app, variant, installerFilePath, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported package type for installation: {packageType}")
        };
    }

    private static string GetSilentArgumentsForExe(string exePath)
    {
        var fileName = Path.GetFileName(exePath).ToLowerInvariant();

        // Common installer heuristic
        if (fileName.Contains("inno") || fileName.Contains("setup"))
        {
            return "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-";
        }
        if (fileName.Contains("nsis"))
        {
            return "/S";
        }

        // Default safe silent switch for NSIS/Inno
        return "/S";
    }

    private static async Task<InstallResult> InstallExeAsync(string exePath, bool silent, CancellationToken cancellationToken)
    {
        try
        {
            var args = silent ? GetSilentArgumentsForExe(exePath) : string.Empty;

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args,
                UseShellExecute = true // Allows standard Windows UAC elevation
            };

            var process = Process.Start(psi);
            if (process == null)
            {
                return new InstallResult(false, "Failed to start installer process.");
            }

            if (silent)
            {
                await process.WaitForExitAsync(cancellationToken);
                return MapExitCode(process.ExitCode, process.Id);
            }

            return new InstallResult(true, "Installer launched.", process.Id);
        }
        catch (Exception ex)
        {
            return new InstallResult(false, $"Execution failed: {ex.Message}");
        }
    }

    private static async Task<InstallResult> InstallMsiAsync(string msiPath, bool silent, CancellationToken cancellationToken)
    {
        try
        {
            var args = silent
                ? $"/i \"{msiPath}\" /qn /norestart"
                : $"/i \"{msiPath}\"";

            var psi = new ProcessStartInfo
            {
                FileName = "msiexec.exe",
                Arguments = args,
                UseShellExecute = true
            };

            var process = Process.Start(psi);
            if (process == null)
            {
                return new InstallResult(false, "Failed to start msiexec process.");
            }

            if (silent)
            {
                await process.WaitForExitAsync(cancellationToken);
                return MapExitCode(process.ExitCode, process.Id);
            }

            return new InstallResult(true, "MSI installer launched.", process.Id);
        }
        catch (Exception ex)
        {
            return new InstallResult(false, $"MSI installation failed: {ex.Message}");
        }
    }

    private static InstallResult MapExitCode(int exitCode, int processId)
    {
        return exitCode switch
        {
            0 => new InstallResult(true, "Installation completed successfully.", processId),
            3010 => new InstallResult(true, "Installation succeeded. A system restart is required to complete setup.", processId, RebootRequired: true),
            1602 => new InstallResult(false, "Installation was cancelled by the user.", processId),
            1618 => new InstallResult(false, "Another installation is already in progress. Complete that installation before proceeding.", processId),
            _ => new InstallResult(false, $"Installer exited with error code {exitCode}.", processId)
        };
    }

    private static async Task<InstallResult> InstallMsixAsync(string packagePath, CancellationToken cancellationToken)
    {
        try
        {
            var pm = new global::Windows.Management.Deployment.PackageManager();
            var packageUri = new Uri(packagePath);

            var deploymentOperation = pm.AddPackageAsync(packageUri, null, global::Windows.Management.Deployment.DeploymentOptions.None);
            await deploymentOperation.AsTask(cancellationToken);

            return new InstallResult(true, "MSIX package installed successfully.");
        }
        catch (Exception ex)
        {
            return new InstallResult(false, $"MSIX package registration failed: {ex.Message}");
        }
    }

    public static bool TrySanitizeId(string id, out string sanitizedId)
    {
        sanitizedId = string.Empty;
        if (string.IsNullOrWhiteSpace(id)) return false;

        var trimmed = id.Trim();
        if (trimmed == "." || trimmed == ".." || trimmed.Contains("..")) return false;
        if (trimmed.Contains('/') || trimmed.Contains('\\')) return false;
        if (trimmed.EndsWith('.') || trimmed.EndsWith(' ')) return false;

        var baseName = Path.GetFileNameWithoutExtension(trimmed);
        if (ReservedDeviceNames.Contains(baseName) || ReservedDeviceNames.Contains(trimmed)) return false;

        if (!Regex.IsMatch(trimmed, @"^[a-zA-Z0-9_\-\.]+$")) return false;

        sanitizedId = trimmed;
        return true;
    }

    public static bool IsPathContainedWithin(string path, string rootDirectory)
    {
        try
        {
            var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullRoot = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private async Task<InstallResult> InstallPortableZipAsync(
        AppItem app,
        AppVariant variant,
        string zipPath,
        CancellationToken cancellationToken)
    {
        string? stagingDir = null;
        try
        {
            var rawId = !string.IsNullOrWhiteSpace(app.Id) ? app.Id : app.Name;
            if (!TrySanitizeId(rawId, out var safeAppId))
            {
                return new InstallResult(false, $"Invalid or unsafe application identifier: '{rawId}'");
            }

            var targetDir = Path.Combine(_appsRootDirectory, safeAppId);
            if (!IsPathContainedWithin(targetDir, _appsRootDirectory))
            {
                return new InstallResult(false, $"Path containment check failed: target directory is outside apps root.");
            }

            // Create temporary staging directory for atomic extraction & zip-bomb protection
            stagingDir = Path.Combine(_appsRootDirectory, $".staging_{Guid.NewGuid():N}");
            Directory.CreateDirectory(stagingDir);

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                if (archive.Entries.Count > MaxZipEntries)
                {
                    throw new InvalidOperationException($"Zip archive contains too many entries ({archive.Entries.Count} > {MaxZipEntries}). Potential zip-bomb rejected.");
                }

                long totalBytes = 0;
                var fullStagingPath = Path.GetFullPath(stagingDir) + Path.DirectorySeparatorChar;

                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    totalBytes += entry.Length;
                    if (totalBytes > MaxUncompressedZipBytes)
                    {
                        throw new InvalidOperationException($"Total uncompressed size exceeds limit ({totalBytes} > {MaxUncompressedZipBytes} bytes). Potential zip-bomb rejected.");
                    }

                    if (entry.CompressedLength > 0)
                    {
                        double ratio = (double)entry.Length / entry.CompressedLength;
                        if (ratio > MaxZipCompressionRatio && entry.Length > 10 * 1024 * 1024)
                        {
                            throw new InvalidOperationException($"Suspiciously high compression ratio ({ratio:F1}x) on entry '{entry.FullName}'. Potential zip-bomb rejected.");
                        }
                    }

                    // Zip-Slip Path Traversal Protection
                    var entryDestination = Path.GetFullPath(Path.Combine(stagingDir, entry.FullName));
                    if (!entryDestination.StartsWith(fullStagingPath, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"Zip-Slip path traversal vulnerability detected! Entry: '{entry.FullName}'");
                    }

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(entryDestination);
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(entryDestination)!);
                        entry.ExtractToFile(entryDestination, overwrite: true);
                    }
                }
            }

            // Staging extraction succeeded; swap into target directory
            if (Directory.Exists(targetDir))
            {
                Directory.Delete(targetDir, true);
            }
            Directory.Move(stagingDir, targetDir);
            stagingDir = null; // Successfully moved

            // Locate primary executable
            var exes = Directory.GetFiles(targetDir, "*.exe", SearchOption.AllDirectories)
                .Where(f => !f.Contains("uninstall", StringComparison.OrdinalIgnoreCase) &&
                            !f.Contains("update", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var primaryExe = exes.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(app.Name, StringComparison.OrdinalIgnoreCase))
                             ?? exes.FirstOrDefault()
                             ?? targetDir;

            _appTracker.RegisterPortableApp(safeAppId, app.Name, app.Version, targetDir, primaryExe);
            CreateStartMenuShortcut(app.Name, primaryExe, targetDir);

            return await Task.FromResult(new InstallResult(true, "Portable package extracted and registered.", InstalledLocation: targetDir));
        }
        catch (Exception ex)
        {
            return new InstallResult(false, $"Extraction failed: {ex.Message}");
        }
        finally
        {
            if (stagingDir != null && Directory.Exists(stagingDir))
            {
                try { Directory.Delete(stagingDir, true); } catch { }
            }
        }
    }

    private static void CreateStartMenuShortcut(string appName, string targetPath, string workingDir)
    {
        try
        {
            var startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Orion Store Apps");
            Directory.CreateDirectory(startMenuDir);
            if (!TrySanitizeId(appName, out var safeName))
            {
                safeName = "App";
            }
            var shortcutPath = Path.Combine(startMenuDir, $"{safeName}.url");

            var content = $"[InternetShortcut]\nURL=file:///{targetPath.Replace('\\', '/')}\nIconIndex=0\nIconFile={targetPath}\n";
            File.WriteAllText(shortcutPath, content);
        }
        catch
        {
            // Non-critical if shortcut creation fails
        }
    }

    public async Task<bool> UninstallAsync(InstalledAppInfo appInfo, CancellationToken cancellationToken = default)
    {
        try
        {
            if (appInfo.Source == "Portable" && !string.IsNullOrEmpty(appInfo.InstallLocation))
            {
                // Verify containment before deletion
                if (IsPathContainedWithin(appInfo.InstallLocation, _appsRootDirectory))
                {
                    if (Directory.Exists(appInfo.InstallLocation))
                    {
                        Directory.Delete(appInfo.InstallLocation, true);
                    }
                    var idToUnregister = appInfo.AppId ?? Path.GetFileName(appInfo.InstallLocation.TrimEnd('\\', '/'));
                    _appTracker.UnregisterPortableApp(idToUnregister);
                    return true;
                }
                return false;
            }

            if (appInfo.Source == "MSIX" && !string.IsNullOrEmpty(appInfo.PackageFullName))
            {
                var pm = new global::Windows.Management.Deployment.PackageManager();
                var op = pm.RemovePackageAsync(appInfo.PackageFullName);
                await op.AsTask(cancellationToken);
                return true;
            }

            var uninst = !string.IsNullOrEmpty(appInfo.QuietUninstallString)
                ? appInfo.QuietUninstallString
                : appInfo.UninstallString;

            if (string.IsNullOrEmpty(uninst)) return false;

            // Safe process launch without cmd.exe
            if (!TryParseUninstallCommand(uninst, out var executable, out var arguments))
            {
                return false;
            }

            var psi = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                UseShellExecute = true
            };

            var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync(cancellationToken);
                return process.ExitCode == 0 || process.ExitCode == 3010;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryParseUninstallCommand(string commandLine, out string executable, out string arguments)
    {
        executable = string.Empty;
        arguments = string.Empty;

        if (string.IsNullOrWhiteSpace(commandLine)) return false;
        var trimmed = commandLine.Trim();

        // 1. Quoted executable path
        if (trimmed.StartsWith("\""))
        {
            var endQuote = trimmed.IndexOf('\"', 1);
            if (endQuote > 1)
            {
                executable = trimmed.Substring(1, endQuote - 1).Trim();
                arguments = trimmed.Substring(endQuote + 1).Trim();
                return true;
            }
            return false;
        }

        // 2. MsiExec command
        if (trimmed.StartsWith("MsiExec", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("msiexec", StringComparison.OrdinalIgnoreCase))
        {
            executable = "msiexec.exe";
            var spaceIdx = trimmed.IndexOf(' ');
            arguments = spaceIdx > 0 ? trimmed.Substring(spaceIdx + 1).Trim() : string.Empty;
            return true;
        }

        // 3. Unquoted executable with arguments
        var exeTokenIdx = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeTokenIdx > 0)
        {
            var endExe = exeTokenIdx + 4;
            executable = trimmed.Substring(0, endExe).Trim();
            arguments = trimmed.Substring(endExe).Trim();
            return true;
        }

        // 4. Standalone unquoted command
        executable = trimmed;
        arguments = string.Empty;
        return true;
    }
}
