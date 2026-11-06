using Orion.Core.Models;

namespace Orion.Core.Services;

public interface IStorefrontEngine
{
    int DailySeed(DateTime? date = null);
    IReadOnlyList<T> Shuffle<T>(IEnumerable<T> items, int seed);
    IReadOnlyList<StoreCollection> BuildStorefront(
        IEnumerable<AppItem> allApps,
        string platform = PlatformConstants.PC,
        IReadOnlyList<ModuleConfig>? modules = null,
        int? seed = null);
    IReadOnlyList<CategoryCard> HomepageCategoryCards(IEnumerable<AppItem> apps, int limit = 10);
}

public class StorefrontEngine : IStorefrontEngine
{
    public int DailySeed(DateTime? date = null)
    {
        var d = date ?? DateTime.UtcNow;
        return d.Year * 10000 + d.Month * 100 + d.Day;
    }

    public IReadOnlyList<T> Shuffle<T>(IEnumerable<T> items, int seed)
    {
        var r = items.ToList();
        if (r.Count <= 1) return r;

        uint a = (uint)seed;
        double Rng()
        {
            unchecked
            {
                a += 0x6D2B79F5;
                uint t = a;
                t = (t ^ (t >> 15)) * (t | 1);
                t = t ^ (t + ((t ^ (t >> 7)) * (t | 61)));
                return ((double)(t ^ (t >> 14))) / 4294967296.0;
            }
        }

        for (int i = r.Count - 1; i >= 1; i--)
        {
            int j = (int)Math.Floor(Rng() * (i + 1));
            (r[i], r[j]) = (r[j], r[i]);
        }

        return r;
    }

    public IReadOnlyList<CategoryCard> HomepageCategoryCards(IEnumerable<AppItem> apps, int limit = 10)
    {
        var appList = apps.ToList();
        var byCategory = appList
            .GroupBy(a => a.Category, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(limit)
            .ToList();

        var cards = new List<CategoryCard>();
        foreach (var g in byCategory)
        {
            var cat = g.Key;
            var icon = cat.ToLowerInvariant() switch
            {
                "utility" or "utilities" => "Toolbox",
                "media" => "Film",
                "music" => "Headphones",
                "productivity" => "Briefcase",
                "social" => "Chat",
                "development" => "Code",
                "privacy" => "Shield",
                "games" => "Games",
                "reading" => "BookOpen",
                _ => "Folder"
            };

            var accent = cat.ToLowerInvariant() switch
            {
                "music" => "#f472b6",
                "media" => "#fb923c",
                "productivity" => "#06b6d4",
                "development" => "#f59e0b",
                "privacy" => "#2dd4bf",
                "social" => "#60a5fa",
                "reading" => "#22c55e",
                _ => "#94a3b8"
            };

            cards.Add(new CategoryCard(
                $"cat-{cat.ToLowerInvariant()}",
                cat,
                icon,
                g.Count(),
                accent,
                new[] { accent, "#0f172a" }
            ));
        }

        return cards;
    }

    public IReadOnlyList<StoreCollection> BuildStorefront(
        IEnumerable<AppItem> allApps,
        string platform = PlatformConstants.PC,
        IReadOnlyList<ModuleConfig>? modules = null,
        int? seed = null)
    {
        List<AppItem> apps;
        if (string.Equals(platform, "All", StringComparison.OrdinalIgnoreCase))
        {
            apps = allApps.ToList();
        }
        else if (Enum.TryParse<PlatformCategory>(platform, true, out var cat))
        {
            apps = allApps.Where(a => PlatformConstants.MatchesCategory(a.Platform, a.TagsList, cat)).ToList();
        }
        else
        {
            var targetPlatform = PlatformConstants.NormalizePlatform(platform);
            var category = targetPlatform switch
            {
                PlatformConstants.PC => PlatformCategory.Windows,
                PlatformConstants.TV => PlatformCategory.Android,
                PlatformConstants.Linux => PlatformCategory.Linux,
                PlatformConstants.MacOS => PlatformCategory.MacOS,
                _ => PlatformCategory.Android
            };
            apps = allApps.Where(a => PlatformConstants.MatchesCategory(a.Platform, a.TagsList, category)).ToList();
        }

        if (apps.Count == 0) return Array.Empty<StoreCollection>();

        var s = seed ?? DailySeed();
        var outCols = new List<StoreCollection>
        {
            new("hero_daily", "Featured Today", CollectionType.Hero, Apps: Shuffle(apps, s).Take(5).ToList()),
            new("recommended_daily", "Recommended For You", CollectionType.Swimlane, Apps: Shuffle(apps, s + 1).Take(10).ToList()),
            new("new_updated", "New & Updated", CollectionType.Swimlane, Apps: apps.AsEnumerable().Reverse().Take(12).ToList(), TotalAppCount: Math.Min(apps.Count, 18))
        };

        var catCards = HomepageCategoryCards(apps, 10);
        if (catCards.Count > 0)
        {
            outCols.Add(new StoreCollection("category_cards", "Browse by Category", CollectionType.CategoryCards, Subtitle: "Click a category to view matching apps", CategoryCards: catCards));
        }

        var byCat = apps.GroupBy(a => a.Category, StringComparer.OrdinalIgnoreCase)
                        .Where(g => g.Count() >= 2)
                        .ToList();

        foreach (var g in byCat)
        {
            outCols.Add(new StoreCollection(
                $"cat_{g.Key.ToLowerInvariant()}",
                $"Top in {g.Key}",
                CollectionType.Swimlane,
                Filter: g.Key,
                Apps: g.Take(12).ToList()
            ));
        }

        var starred = apps.Where(a => (a.Stars ?? 0) > 0).OrderByDescending(a => a.Stars).ToList();
        if (starred.Count >= 6)
        {
            outCols.Add(new StoreCollection("most_popular", "Most Popular", CollectionType.Ranked, Subtitle: "Most starred on GitHub", Apps: starred.Take(12).ToList()));
        }

        return outCols;
    }
}

