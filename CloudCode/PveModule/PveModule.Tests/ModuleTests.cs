using System.Collections.Generic;
using System.IO;
using System.Linq;
using MobileIdleBuilder;
using MobileIdleBuilder.PveModule;
using Xunit;

// Module-only checks; the rules themselves are tested in Unity (Assets/Scripts/Tests/PveRulesTests.cs).
public class ModuleTests
{
    [Fact]
    public void EmbeddedGameData_HasTheLeagueLadderAndNameFilter()
    {
        var cfg = PveGameConfig.Embedded;
        Assert.NotNull(cfg.Pve);
        Assert.Equal(new[] { "bronze", "silver", "gold", "platinum", "diamond", "champion" }, cfg.Pve.leagues.Select(l => l.id));
        Assert.All(cfg.Pve.leagues, l => Assert.NotEmpty(l.rewards));
        Assert.True(cfg.NamePatterns.Count > 20);
        Assert.Equal(NameCheck.Blocked, PlayerNameRules.Validate("F_U_C_K", cfg.NamePatterns));
        Assert.Equal(NameCheck.Ok, PlayerNameRules.Validate("Therapist", cfg.NamePatterns));
    }

    [Fact]
    public void Serialize_WritesPublicFields_AndRoundTrips()
    {
        var p = new PveProfile { league = "gold", lastWeek = "2026-11-27", bracketId = "pve_2026-11-27_gold_1" };
        p.pendingRewards.Add(new PveReward { week = "2026-11-27", crystals = 300, stars = 10, rank = 1, size = 5 });
        string json = PveStore.Serialize(p);
        Assert.Contains("\"league\":\"gold\"", json);
        Assert.Contains("\"crystals\":300", json);
        var back = PveStore.Deserialize<PveProfile>(json);
        Assert.Equal("gold", back.league);
        Assert.Equal(10, back.pendingRewards[0].stars);
    }

    [Fact]
    public void Deserialize_AcceptsAStoredStringOrAJsonObjectValue()
    {
        string json = PveStore.Serialize(new PveSettings { smallBrackets = true });
        Assert.True(PveStore.Deserialize<PveSettings>(json).smallBrackets);
        // Cloud Save may hand the value back as a JSON token object; ToString() yields its JSON.
        Assert.True(PveStore.Deserialize<PveSettings>(new Wrapper(json)).smallBrackets);
        Assert.Null(PveStore.Deserialize<PveSettings>(null));
    }

    sealed class Wrapper { readonly string _j; public Wrapper(string j) => _j = j; public override string ToString() => _j; }

    [Fact]
    public void Parse_ToleratesMissingSections()
    {
        var cfg = PveGameConfig.Parse("{\"game_config\":{}}");
        Assert.Null(cfg.Pve);
        Assert.Empty(cfg.NamePatterns);
    }
}
