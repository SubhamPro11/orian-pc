using Orion.Core.Models;
using Orion.Core.Services;
using Xunit;

namespace Orion.Tests;

public class StorefrontAndXpTests
{
    private readonly StorefrontEngine _engine = new();
    private readonly XpEngine _xp = new();

    [Fact]
    public void DailySeed_CalculatesConsistentSeed()
    {
        var dt = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
        var seed = _engine.DailySeed(dt);
        Assert.Equal(20261105, seed);
    }

    [Fact]
    public void Shuffle_ProducesDeterministicOrderForSameSeed()
    {
        var items = Enumerable.Range(1, 20).ToList();
        var s1 = _engine.Shuffle(items, 42);
        var s2 = _engine.Shuffle(items, 42);

        Assert.Equal(s1, s2);
        Assert.NotEqual(items, s1); // Confirms items are scrambled
    }

    [Fact]
    public void BuildStorefront_GeneratesExpectedCollections()
    {
        var apps = new List<AppItem>
        {
            new(Id: "w1", Name: "Flow Launcher", Platform: "PC", Category: "Productivity", Stars: 5000),
            new(Id: "w2", Name: "PowerToys", Platform: "PC", Category: "Utility", Stars: 90000),
            new(Id: "w3", Name: "Notepad++", Platform: "PC", Category: "Utility", Stars: 20000),
            new(Id: "w4", Name: "7-Zip", Platform: "PC", Category: "Utility", Stars: 15000),
            new(Id: "w5", Name: "VLC", Platform: "PC", Category: "Media", Stars: 3000),
            new(Id: "w6", Name: "OBS Studio", Platform: "PC", Category: "Media", Stars: 55000)
        };

        var collections = _engine.BuildStorefront(apps, PlatformConstants.PC, seed: 12345);

        Assert.NotEmpty(collections);
        Assert.Contains(collections, c => c.Id == "hero_daily");
        Assert.Contains(collections, c => c.Id == "recommended_daily");
        Assert.Contains(collections, c => c.Id == "new_updated");
        Assert.Contains(collections, c => c.Id == "category_cards");
        Assert.Contains(collections, c => c.Id == "most_popular");
    }

    [Fact]
    public void XpEngine_CalculatesProgressionAccurately()
    {
        // Level 1 starts at 0, Level 2 starts at Step(1) = 50 + 10(1) + 1 = 61
        Assert.Equal(61, _xp.Step(1));
        Assert.Equal(0, _xp.LevelStart(1));
        Assert.Equal(61, _xp.LevelStart(2));

        Assert.Equal(1, _xp.GetLevel(0));
        Assert.Equal(1, _xp.GetLevel(60));
        Assert.Equal(2, _xp.GetLevel(61));

        Assert.Equal("Cadet", _xp.GetTitle(1));
        Assert.Equal("Bronze pilot", _xp.GetTitle(10));
        Assert.Equal("Silver pilot", _xp.GetTitle(30));
        Assert.Equal("Gold pilot", _xp.GetTitle(50));
    }
}

