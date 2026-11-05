using System.Text.RegularExpressions;

namespace Orion.Core.Services;

public interface IReleaseNotesCleaner
{
    string CleanNotes(string markdown, int maxLines = 40);
}

public class ReleaseNotesCleaner : IReleaseNotesCleaner
{
    private static readonly Regex HtmlComments = new(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex CodeBlocks = new(@"```.*?```", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex DetailsTags = new(@"<details>.*?</details>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MarkdownImages = new(@"!\[[^\]]*\]\([^)]*\)", RegexOptions.Compiled);
    private static readonly Regex MarkdownLinks = new(@"\[([^\]]+)\]\([^)]*\)", RegexOptions.Compiled);
    private static readonly Regex HtmlBreaks = new(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HtmlTags = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex DropHeadings = new(@"^(full changelog|new contributors|assets|checksums?|sha-?256|md5|download|installation)\b.*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AuthorCredits = new(@"\s+by @[\w-]+( in \S+)?", RegexOptions.Compiled);
    private static readonly Regex BareUrls = new(@"https?://\S+", RegexOptions.Compiled);
    private static readonly Regex EmojiShortcodes = new(@"(?<!\w):[a-z0-9_+-]+:(?!\w)", RegexOptions.Compiled);
    private static readonly Regex FormattingChars = new(@"(\*\*|__|~~|`)", RegexOptions.Compiled);
    private static readonly Regex SingleAsterisks = new(@"(?<![\w*])\*(?!\s)([^*]+)\*", RegexOptions.Compiled);
    private static readonly Regex ListBullets = new(@"^[-*+]\s+(\[[ xX]\]\s+)?", RegexOptions.Compiled);

    public string CleanNotes(string markdown, int maxLines = 40)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;

        var text = markdown.Replace("\r", "");
        text = HtmlComments.Replace(text, "");
        text = CodeBlocks.Replace(text, "");
        text = DetailsTags.Replace(text, "");
        text = MarkdownImages.Replace(text, "");
        text = MarkdownLinks.Replace(text, "$1");
        text = HtmlBreaks.Replace(text, "\n");
        text = HtmlTags.Replace(text, "");

        var lines = text.Split('\n', StringSplitOptions.None)
            .Select(l => l.Trim())
            .ToList();

        var output = new List<string>();
        bool skipping = false;

        foreach (var line in lines)
        {
            var isHeading = line.StartsWith('#');
            var cleanLine = line.TrimStart('#').Trim();

            if (isHeading)
            {
                skipping = DropHeadings.IsMatch(cleanLine);
            }

            if (skipping) continue;
            if (DropHeadings.IsMatch(cleanLine)) continue;

            output.Add(line);
        }

        var cleaned = output
            .Where(l => !l.StartsWith('|') && !Regex.IsMatch(l, @"^[-*_=]{3,}$"))
            .Select(l =>
            {
                var s = AuthorCredits.Replace(l, "");
                s = BareUrls.Replace(s, "");
                s = EmojiShortcodes.Replace(s, "");
                s = FormattingChars.Replace(s, "");
                s = SingleAsterisks.Replace(s, "$1");
                s = s.Replace("()", "").TrimEnd(' ', ':', ',');
                var m = ListBullets.Match(s);
                if (m.Success)
                {
                    s = "• " + s[m.Length..].Trim();
                }
                return s;
            })
            .Where(l => l != "•" && !string.IsNullOrWhiteSpace(l))
            .Take(maxLines)
            .ToList();

        return string.Join("\n", cleaned);
    }
}

