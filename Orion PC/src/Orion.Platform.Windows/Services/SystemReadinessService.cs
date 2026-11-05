using System.Runtime.InteropServices;

namespace Orion.Platform.Windows.Services;

public record SystemReadinessReport(
    bool IsReady,
    string OsVersion,
    string Architecture,
    bool HasStorageAccess,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors
);

public interface ISystemReadinessService
{
    SystemReadinessReport CheckReadiness();
}

public class SystemReadinessService : ISystemReadinessService
{
    private static readonly Version MinWindowsVersion = new(10, 0, 17763, 0);

    public SystemReadinessReport CheckReadiness()
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // 1. OS Version Check
        var osVersion = Environment.OSVersion.Version;
        var osVersionStr = $"Windows {osVersion.Major}.{osVersion.Minor} (Build {osVersion.Build})";

        if (osVersion < MinWindowsVersion)
        {
            errors.Add($"Windows version {osVersionStr} is unsupported. Orion Store requires Windows 10 version 1809 (Build 17763) or higher.");
        }

        // 2. CPU Architecture
        var arch = RuntimeInformation.OSArchitecture.ToString();
        if (RuntimeInformation.OSArchitecture != System.Runtime.InteropServices.Architecture.X64 &&
            RuntimeInformation.OSArchitecture != System.Runtime.InteropServices.Architecture.Arm64 &&
            RuntimeInformation.OSArchitecture != System.Runtime.InteropServices.Architecture.X86)
        {
            warnings.Add($"Uncommon OS architecture detected: {arch}. Some native Windows apps may not run.");
        }

        // 3. LocalAppData Storage Write Access
        bool hasStorageAccess = false;
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var testDir = Path.Combine(localAppData, "OrionStore", ".readiness_probe");
            Directory.CreateDirectory(testDir);
            var testFile = Path.Combine(testDir, "test.tmp");
            File.WriteAllText(testFile, "probe");
            File.Delete(testFile);
            Directory.Delete(testDir);
            hasStorageAccess = true;
        }
        catch (Exception ex)
        {
            errors.Add($"Cannot write to LocalApplicationData directory: {ex.Message}");
        }

        bool isReady = errors.Count == 0;
        return new SystemReadinessReport(isReady, osVersionStr, arch, hasStorageAccess, warnings, errors);
    }
}

