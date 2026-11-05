using System.Diagnostics;
using System.IO.Compression;
using Orion.Core.Models;
using Orion.Core.Services;

namespace Orion.Platform.Windows.Services;

public record InstallResult(
    bool Success,
    string? Message = null,
    int? ProcessId = null,
    string? InstalledLocation = null
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
        // Strictly enforce Windows validation first
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

    private static async Task<InstallResult> InstallExeAsync(string exePath, bool silent, CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true, // Allows standard UAC elevation prompt if required by the installer
                Arguments = silent ? "/S /SILENT /VERYSILENT /qn" : string.Empty
            };

            var process = Process.Start(psi);
            if (process == null)
            {
                return new InstallResult(false, "Failed to start installer process.");
            }

            if (silent)
            {
                await process.WaitForExitAsync(cancellationToken);
                return process.ExitCode == 0
                    ? new InstallResult(true, "Installation completed successfully.", process.Id)
                    : new InstallResult(false, $"Installer exited with code {process.ExitCode}.", process.Id);
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
                return process.ExitCode == 0
                    ? new InstallResult(true, "MSI installation completed.", process.Id)
                    : new InstallResult(false, $"MSI installer exited with code {process.ExitCode}.", process.Id);
            }

            return new InstallResult(true, "MSI installer launched.", process.Id);
        }
        catch (Exception ex)
        {
            return new InstallResult(false, $"MSI installation failed: {ex.Message}");
        }
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

    private async Task<InstallResult> InstallPortableZipAsync(
        AppItem app,
        AppVariant variant,
        string zipPath,
        CancellationToken cancellationToken)
    {
        try
        {
            var safeAppId = SanitizeFileName(app.Id.Length > 0 ? app.Id : app.Name);
            var targetDir = Path.Combine(_appsRootDirectory, safeAppId);

            if (Directory.Exists(targetDir))
            {
                Directory.Delete(targetDir, true);
            }
            Directory.CreateDirectory(targetDir);

            // Zip-Slip Safe Extraction
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var fullDestDirectory = Path.GetFullPath(targetDir) + Path.DirectorySeparatorChar;

                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var entryDestination = Path.GetFullPath(Path.Combine(targetDir, entry.FullName));
                    if (!entryDestination.StartsWith(fullDestDirectory, StringComparison.OrdinalIgnoreCase))
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

            // Find primary executable in extracted files
            var exes = Directory.GetFiles(targetDir, "*.exe", SearchOption.AllDirectories)
                .Where(f => !f.Contains("uninstall", StringComparison.OrdinalIgnoreCase) &&
                            !f.Contains("update", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var primaryExe = exes.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(app.Name, StringComparison.OrdinalIgnoreCase))
                             ?? exes.FirstOrDefault()
                             ?? targetDir;

            // Register in portable tracking database
            _appTracker.RegisterPortableApp(app.Id, app.Name, app.Version, targetDir, primaryExe);

            // Create Start Menu Shortcut
            CreateStartMenuShortcut(app.Name, primaryExe, targetDir);

            return await Task.FromResult(new InstallResult(true, "Portable package extracted and registered.", InstalledLocation: targetDir));
        }
        catch (Exception ex)
        {
            return new InstallResult(false, $"Extraction failed: {ex.Message}");
        }
    }

    private static void CreateStartMenuShortcut(string appName, string targetPath, string workingDir)
    {
        try
        {
            var startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Orion Store Apps");
            Directory.CreateDirectory(startMenuDir);
            var shortcutPath = Path.Combine(startMenuDir, $"{SanitizeFileName(appName)}.url");

            // Use .url shortcut format pointing to the executable file
            var content = $"[InternetShortcut]\nURL=file:///{targetPath.Replace('\\', '/')}\nIconIndex=0\nIconFile={targetPath}\n";
            File.WriteAllText(shortcutPath, content);
        }
        catch
        {
            // Non-critical if shortcut creation fails
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }

    public async Task<bool> UninstallAsync(InstalledAppInfo appInfo, CancellationToken cancellationToken = default)
    {
        try
        {
            if (appInfo.Source == "Portable" && !string.IsNullOrEmpty(appInfo.InstallLocation))
            {
                if (Directory.Exists(appInfo.InstallLocation))
                {
                    Directory.Delete(appInfo.InstallLocation, true);
                }
                _appTracker.UnregisterPortableApp(appInfo.DisplayName);
                return true;
            }

            if (appInfo.Source == "MSIX" && !string.IsNullOrEmpty(appInfo.PackageFamilyName))
            {
                var pm = new global::Windows.Management.Deployment.PackageManager();
                var op = pm.RemovePackageAsync(appInfo.PackageFamilyName);
                await op.AsTask(cancellationToken);
                return true;
            }

            var uninst = !string.IsNullOrEmpty(appInfo.QuietUninstallString)
                ? appInfo.QuietUninstallString
                : appInfo.UninstallString;

            if (string.IsNullOrEmpty(uninst)) return false;

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {uninst}",
                UseShellExecute = true
            };

            var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync(cancellationToken);
                return process.ExitCode == 0;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}

