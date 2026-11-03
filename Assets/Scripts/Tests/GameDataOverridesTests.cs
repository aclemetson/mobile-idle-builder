using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Covers the pure parse + apply seam of the runtime balance-override layer. The Remote Config fetch
    /// and the boot wiring (ItemDatabase/ResearchService/GameBootstrap) are integration-only, so these
    /// tests drive <see cref="GameDataOverrides"/> directly with explicit entry arrays against in-memory
    /// ScriptableObject instances. Verifies allow-listing, id-matching, int rounding, and that
    /// blank/garbage/unknown input fails closed without throwing.
    /// </summary>
    [TestFixture]
    public class GameDataOverridesTests
    {
        // ── Parse ────────────────────────────────────────────────────────────────

        [Test]
        public void Parse_ValidBlob_PopulatesArrays()
        {
            var blob = GameDataOverrides.Parse(
                "{\"config\":[{\"field\":\"prestigeBaseValue\",\"value\":600}]," +
                "\"items\":[{\"id\":\"iron\",\"field\":\"baseSellValue\",\"value\":2}]," +
                "\"research\":[{\"id\":\"r1\",\"field\":\"durationSeconds\",\"value\":300}]}");

            Assert.IsNotNull(blob);
            Assert.AreEqual(1, blob.config.Length);
            Assert.AreEqual("prestigeBaseValue", blob.config[0].field);
            Assert.AreEqual(600f, blob.config[0].value);
            Assert.AreEqual("iron", blob.items[0].id);
            Assert.AreEqual("r1", blob.research[0].id);
        }

        [Test]
        public void Parse_BlankOrGarbage_ReturnsNull_NeverThrows()
        {
            Assert.IsNull(GameDataOverrides.Parse(null));
            Assert.IsNull(GameDataOverrides.Parse(""));
            Assert.IsNull(GameDataOverrides.Parse("   "));
            Assert.IsNull(GameDataOverrides.Parse("not json at all"));
        }

        [Test]
        public void Parse_EmptyObject_ReturnsBlobWithNullArrays()
        {
            var blob = GameDataOverrides.Parse("{}");
            Assert.IsNotNull(blob);
            Assert.IsNull(blob.config);
            Assert.IsNull(blob.items);
        }

        // ── Config ───────────────────────────────────────────────────────────────

        [Test]
        public void ApplyConfig_KnownFields_AreSet()
        {
            var cfg = ScriptableObject.CreateInstance<GameConfigSO>();
            var entries = new[]
            {
                new GameDataOverrides.ConfigEntry { field = "prestigeBaseValue", value = 600f },
                new GameDataOverrides.ConfigEntry { field = "startingEntropy",   value = 300f },
            };

            int applied = GameDataOverrides.ApplyConfig(cfg, entries);

            Assert.AreEqual(2, applied);
            Assert.AreEqual(600f, cfg.prestigeBaseValue);
            Assert.AreEqual(300L, cfg.startingEntropy); // float -> long cast
        }

        [Test]
        public void ApplyConfig_UnknownField_Ignored()
        {
            var cfg = ScriptableObject.CreateInstance<GameConfigSO>();
            float before = cfg.prestigeBaseValue;
            var entries = new[]
            {
                new GameDataOverrides.ConfigEntry { field = "doesNotExist", value = 999f },
            };

            int applied = GameDataOverrides.ApplyConfig(cfg, entries);

            Assert.AreEqual(0, applied);
            Assert.AreEqual(before, cfg.prestigeBaseValue);
        }

        [Test]
        public void ApplyConfig_NullArgs_NoThrow()
        {
            var cfg = ScriptableObject.CreateInstance<GameConfigSO>();
            Assert.AreEqual(0, GameDataOverrides.ApplyConfig(null, new GameDataOverrides.ConfigEntry[0]));
            Assert.AreEqual(0, GameDataOverrides.ApplyConfig(cfg, null));
        }

        // ── Items ────────────────────────────────────────────────────────────────

        [Test]
        public void ApplyItems_MatchesById_OnlyTargetChanges()
        {
            var iron   = ScriptableObject.CreateInstance<ItemSO>(); iron.id = "iron";   iron.baseSellValue = 1f;
            var copper = ScriptableObject.CreateInstance<ItemSO>(); copper.id = "copper"; copper.baseSellValue = 1f;
            var entries = new[]
            {
                new GameDataOverrides.EntityEntry { id = "iron", field = "baseSellValue", value = 5f },
                new GameDataOverrides.EntityEntry { id = "unknown", field = "baseSellValue", value = 99f },
            };

            int applied = GameDataOverrides.ApplyItems(new[] { iron, copper }, entries);

            Assert.AreEqual(1, applied);
            Assert.AreEqual(5f, iron.baseSellValue);
            Assert.AreEqual(1f, copper.baseSellValue); // untouched
        }

        // ── Research ───────────────────────────────────────────────────────────────

        [Test]
        public void ApplyResearch_FloatValue_RoundsToInt()
        {
            var r = ScriptableObject.CreateInstance<ResearchSO>(); r.id = "r1";
            var entries = new[]
            {
                new GameDataOverrides.EntityEntry { id = "r1", field = "durationSeconds",  value = 299.6f },
                new GameDataOverrides.EntityEntry { id = "r1", field = "costBaseCurrency",  value = 1200f },
            };

            int applied = GameDataOverrides.ApplyResearch(new[] { r }, entries);

            Assert.AreEqual(2, applied);
            Assert.AreEqual(300, r.durationSeconds); // 299.6 -> 300
            Assert.AreEqual(1200, r.costBaseCurrency);
        }

        [Test]
        public void ApplyResearch_UnknownId_Ignored()
        {
            var r = ScriptableObject.CreateInstance<ResearchSO>(); r.id = "r1"; r.durationSeconds = 100;
            var entries = new[]
            {
                new GameDataOverrides.EntityEntry { id = "nope", field = "durationSeconds", value = 1f },
            };

            int applied = GameDataOverrides.ApplyResearch(new[] { r }, entries);

            Assert.AreEqual(0, applied);
            Assert.AreEqual(100, r.durationSeconds);
        }
    }
}
