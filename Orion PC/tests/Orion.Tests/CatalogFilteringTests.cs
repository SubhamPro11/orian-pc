using System.Text.Json;
using Orion.Core.Models;
using Orion.Core.Services;
using Xunit;

namespace Orion.Tests;

public class CatalogFilteringTests
{
    private readonly CatalogProcessor _processor;

    public CatalogFilteringTests()
    {
        var versionService = new VersionService();
        var assetResolver = new AssetResolver();
        _processor = new CatalogProcessor(versionService, assetResolver);
    }

    [Fact]
    public void FilterByPlatform_CorrectlySeparatesPlatforms()
    {
        var apps = new List<AppItem>
        {
            new(Id: "win1", Name: "Windows App 1", Platform: "PC"),
            new(Id: "win2", Name: "Windows App 2", Platform: "Windows"),
            new(Id: "win3", Name: "Windows App 3", Platform: "Android", Tags: new[] { "desktop", "windows" }),
            new(Id: "droid1", Name: "Android App 1", Platform: "Android"),
            new(Id: "droid2", Name: "Android App 2", Platform: "Android"),
            new(Id: "tv1", Name: "TV App", Platform: "TV"),
            new(Id: "linux1", Name: "Linux App", Platform: "Linux"),
            new(Id: "mac1", Name: "Mac App", Platform: "macOS")
        };

        var windowsApps = _processor.FilterByPlatform(apps, PlatformCategory.Windows);
        Assert.Equal(3, windowsApps.Count);
        Assert.All(windowsApps, a => Assert.True(a.IsWindowsApp));

        var androidApps = _processor.FilterByPlatform(apps, PlatformCategory.Android);
        Assert.Equal(3, androidApps.Count); // 2 Android + 1 TV

        var linuxApps = _processor.FilterByPlatform(apps, PlatformCategory.Linux);
        Assert.Single(linuxApps);
        Assert.Equal("linux1", linuxApps[0].Id);

        var macApps = _processor.FilterByPlatform(apps, PlatformCategory.MacOS);
        Assert.Single(macApps);
        Assert.Equal("mac1", macApps[0].Id);

        var allApps = _processor.FilterByPlatform(apps, PlatformCategory.All);
        Assert.Equal(8, allApps.Count);
    }

    [Theory]
    [InlineData("javascript:alert(1)", "#")]
    [InlineData("file:///C:/Windows/System32/calc.exe", "#")]
    [InlineData("data:text/html,<script>alert(1)</script>", "#")]
    [InlineData("ms-msdt:/id DEV.123", "#")]
    [InlineData("https://github.com/app/releases", "https://github.com/app/releases")]
    [InlineData("http://example.com/app.exe", "http://example.com/app.exe")]
    [InlineData("#", "#")]
    [InlineData("", "#")]
    public void SanitizeUrl_OnlyPermitsHttpAndHttps(string input, string expected)
    {
        var sanitized = CatalogProcessor.SanitizeUrl(input);
        Assert.Equal(expected, sanitized);
    }

    [Fact]
    public void SanitizeApp_SanitizesJavascriptUrlsAndCoercesFields()
    {
        var jsonText = """
        {
            "id": "unsafe-app",
            "name": "Unsafe App",
            "icon": "javascript:alert(1)",
            "downloadUrl": "javascript:void(0)",
            "category": "UTILITY",
            "platform": "windows",
            "tags": ["win32"]
        }
        """;

        using var doc = JsonDocument.Parse(jsonText);
        var sanitized = _processor.SanitizeApp(doc.RootElement);

        Assert.NotNull(sanitized);
        Assert.Equal("", sanitized.Icon);
        Assert.Equal("#", sanitized.DownloadUrl);
        Assert.Equal("Utility", sanitized.Category);
        Assert.Equal(PlatformConstants.PC, sanitized.Platform);
        Assert.True(sanitized.IsWindowsApp);
    }

    [Fact]
    public void Search_RanksRelevantAppsCorrectly()
    {
        var apps = new List<AppItem>
        {
            new(Id: "app1", Name: "Calculator Plus", Author: "Dev A", Category: "Utility"),
            new(Id: "calc-pro", Name: "Advanced Tool", Author: "Calc Team", Category: "Utility"),
            new(Id: "app3", Name: "Photo Editor", Author: "Dev B", Category: "Media")
        };

        var searchResults = _processor.Search("calc", "All", SortOption.Home, apps);
        Assert.Equal(2, searchResults.Count);
        // "Calculator Plus" starts with "calc" -> higher score
        Assert.Equal("Calculator Plus", searchResults[0].Name);
    }

    [Theory]
    [InlineData("1024 B", 1024)]
    [InlineData("1.5 MB", 1.5 * 1024 * 1024)]
    [InlineData("500 KB", 500 * 1024)]
    [InlineData("2 GB", 2.0 * 1024 * 1024 * 1024)]
    [InlineData("Varies", 0)]
    public void ParseSize_ParsesCorrectBytes(string input, double expectedApprox)
    {
        var bytes = _processor.ParseSize(input);
        Assert.True(Math.Abs(bytes - expectedApprox) < 100);
    }

    [Theory]
    [InlineData(1024L, "1 KB")]
    [InlineData(1048576L, "1 MB")]
    [InlineData(1572864L, "1.5 MB")]
    [InlineData(0L, "Varies")]
    [InlineData(null, "Varies")]
    public void FormatSize_FormatsCorrectly(long? bytes, string expected)
    {
        var formatted = _processor.FormatSize(bytes);
        Assert.Equal(expected, formatted);
    }
}
