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
    public void EmbeddedRewards_PayStrings_InEveryBand_MoreInHigherLeagues()
    {
        var leagues = PveGameConfig.Embedded.Pve.leagues;
        Assert.All(leagues, l => Assert.All(l.rewards, b => Assert.True(b.strings > 0, l.id)));
        for (int i = 1; i < leagues.Length; i++)
            Assert.True(leagues[i].rewards[0].strings > leagues[i - 1].rewards[0].strings, leagues[i].id);
        Assert.Equal(60, PveRules.RewardFor(leagues[0], 1, 100).strings);
        Assert.Equal(5, PveRules.RewardFor(leagues[0], 90, 100).strings);   // finished, outside every band

        var back = PveStore.Deserialize<PveReward>(PveStore.Serialize(new PveReward { week = "w", strings = 35 }));
        Assert.Equal(35, back.strings);
    }

    [Fact]
    public void Cosmetics_OnlyKnownIdsOfTheRightKindAreValid_AndTheLookRoundTrips()
    {
        Assert.True(PveCosmetics.IsValid("", CosmeticKind.NameColor));
        Assert.True(PveCosmetics.IsValid("color_gold", CosmeticKind.NameColor));
        Assert.False(PveCosmetics.IsValid("color_gold", CosmeticKind.Title), "a colour id is not a title");
        Assert.False(PveCosmetics.IsValid("<b>hi</b>", CosmeticKind.Title), "no free text");
        Assert.Equal("String Theorist", PveCosmetics.TitleText("title_string_theorist"));

        var m = PveStore.Deserialize<PveMember>(PveStore.Serialize(new PveMember { nameColor = "color_rose", title = "title_idle_physicist" }));
        Assert.Equal("color_rose", m.nameColor);
        Assert.Equal("title_idle_physicist", m.title);
        var row = PveStore.Deserialize<PveBoardRow>(PveStore.Serialize(new PveBoardRow { nameColor = "color_lime" }));
        Assert.Equal("color_lime", row.nameColor);
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

    [Fact]
    public void DeleteMyData_RemovesOnlyTheCallerFromTheBracketMeta_AndTheResponseRoundTrips()
    {
        var meta = new PveBracketMeta { id = "pve_2026-11-27_bronze_1", playerIds = new List<string> { "a", "me", "b" } };
        var stored = PveStore.Deserialize<PveBracketMeta>(PveStore.Serialize(meta));
        var written = PveRules.WithoutPlayer(stored, "me");
        Assert.Equal(new[] { "a", "b" }, written.playerIds);
        Assert.Null(PveRules.WithoutPlayer(written, "me"));   // already gone: nothing to write

        var back = PveStore.Deserialize<PveDeleteResponse>(PveStore.Serialize(new PveDeleteResponse { deleted = true }));
        Assert.True(back.deleted);
    }

    [Fact]
    public void Crystals_MigrationIsCapped_AndTheLedgerAndWalletRoundTrip()
    {
        Assert.Equal(0, CrystalRules.MigrationAmount(-5));
        Assert.Equal(1234, CrystalRules.MigrationAmount(1234));
        Assert.Equal(CrystalRules.MigrationCap, CrystalRules.MigrationAmount(long.MaxValue));

        var ledger = PveStore.Deserialize<CrystalLedger>(PveStore.Serialize(
            new CrystalLedger { migrated = true, migratedAmount = 600, migratedUtc = "2026-11-28T00:00:00Z" }));
        Assert.True(ledger.migrated);
        Assert.Equal(600, ledger.migratedAmount);

        string json = PveStore.Serialize(new CrystalWalletResponse { balance = 3200, migrated = true });
        Assert.Contains("\"balance\":3200", json);
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
