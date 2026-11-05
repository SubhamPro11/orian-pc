using Orion.Core.Services;
using Xunit;

namespace Orion.Tests;

public class ReleaseNotesCleanerTests
{
    private readonly ReleaseNotesCleaner _cleaner = new();

    [Fact]
    public void CleanNotes_StripsHtmlAndMarkdownLinksAndBoilerplate()
    {
        const string rawMarkdown = """
        <!-- HTML Comment to drop -->
        # What's Changed
        * Fixed critical UI crash by @developer in https://github.com/org/repo/pull/123
        * Added support for Windows 11 Mica backdrop in #456
        * Improved performance: <br> Reduced memory usage by 20%
        <details>
        <summary>Diagnostics info</summary>
        Ignore this block.
        </details>
        
        ## Full Changelog
        Ignore everything under this section
        * Commit 1
        * Commit 2
        
        ## Assets
        Ignore assets
        """;

        var cleaned = _cleaner.CleanNotes(rawMarkdown);

        Assert.Contains("Fixed critical UI crash", cleaned);
        Assert.Contains("Added support for Windows 11 Mica backdrop", cleaned);
        Assert.Contains("Reduced memory usage by 20%", cleaned);
        Assert.DoesNotContain("Full Changelog", cleaned);
        Assert.DoesNotContain("Commit 1", cleaned);
        Assert.DoesNotContain("HTML Comment", cleaned);
        Assert.DoesNotContain("Diagnostics info", cleaned);
        Assert.DoesNotContain("by @developer", cleaned);
        Assert.DoesNotContain("https://github.com", cleaned);
    }
}

