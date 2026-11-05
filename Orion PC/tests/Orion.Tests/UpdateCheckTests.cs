using Orion.Core.Models;
using Orion.Core.Services;
using Orion.Platform.Windows.Services;
using Xunit;

namespace Orion.Tests;

public class UpdateCheckTests
{
    private readonly VersionService _versionService = new();
    private readonly WindowsAppTracker _appTracker;

    public UpdateCheckTests()
    {
        _appTracker = new WindowsAppTracker(_versionService);
    }

    [Fact]
    public void UpdatePipeline_DetectsUpdatesAccurately()
    {
        var app = new AppItem(
            Id: "flow-launcher",
            Name: "Flow Launcher",
            Platform: "PC",
            Version: "1.17.0",
            LatestVersion: "1.17.0",
            AvailableVersions: new[]
            {
                new VersionOption("Stable", "1.17.0", "2026-01-01", Array.Empty<AppVariant>()),
                new VersionOption("Beta", "1.18.0-beta1", "2026-02-01", Array.Empty<AppVariant>())
            }
        );

        // Case 1: Older version installed on Stable stream
        Assert.True(_versionService.HasAvailableUpdate(app, "1.16.0", "Stable"));

        // Case 2: Current version installed on Stable stream
        Assert.False(_versionService.HasAvailableUpdate(app, "1.17.0", "Stable"));

        // Case 3: Newer version installed (e.g. dev build)
        Assert.False(_versionService.HasAvailableUpdate(app, "1.17.1", "Stable"));

        // Case 4: On Beta stream, 1.18.0-beta1 is an update over 1.17.0
        Assert.True(_versionService.HasAvailableUpdate(app, "1.17.0", "Beta"));
    }
}

