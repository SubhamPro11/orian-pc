using Orion.Core.Models;
using Orion.Core.Services;
using Orion.Platform.Windows.Services;
using Xunit;

namespace Orion.Tests;

public class WindowsAppTrackerTests
{
    private readonly WindowsAppTracker _tracker;

    public WindowsAppTrackerTests()
    {
        var versionService = new VersionService();
        _tracker = new WindowsAppTracker(versionService);
    }

    [Fact]
    public void GetInstalledApps_ScansRegistryAndReturnsRealApps()
    {
        var installed = _tracker.GetInstalledApps(forceRefresh: true);

        Assert.NotNull(installed);
        Assert.NotEmpty(installed);

        // Every Windows machine with .NET installed has registry uninstall entries
        Assert.Contains(installed, app => !string.IsNullOrWhiteSpace(app.DisplayName));
    }

    [Fact]
    public void RegisterAndUnregisterPortableApp_ManagesDatabaseCorrectly()
    {
        var testId = $"portable_test_{Guid.NewGuid():N}";
        var testPath = Path.Combine(Path.GetTempPath(), testId);
        Directory.CreateDirectory(testPath);
        var exePath = Path.Combine(testPath, "run.exe");
        File.WriteAllText(exePath, "dummy binary");

        try
        {
            _tracker.RegisterPortableApp(testId, "Test Portable App", "2.1.0", testPath, exePath);

            var dummyApp = new AppItem(Id: testId, Name: "Test Portable App", Platform: "PC");
            var found = _tracker.FindInstalledApp(dummyApp);

            Assert.NotNull(found);
            Assert.Equal("2.1.0", found.DisplayVersion);
            Assert.Equal("Portable", found.Source);

            _tracker.UnregisterPortableApp(testId);
            var foundAfterUnregister = _tracker.FindInstalledApp(dummyApp);
            Assert.Null(foundAfterUnregister);
        }
        finally
        {
            if (Directory.Exists(testPath)) Directory.Delete(testPath, true);
        }
    }
}

