using System.Collections.Generic;
using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Verifies the pure feature-flag resolution layer: precedence (remote &gt; cache &gt; default),
    /// type parsing, and the cache/remote merge. The MonoBehaviour service is a thin wrapper over
    /// these helpers, so testing them covers the resolution contract without Unity or the network.
    /// </summary>
    [TestFixture]
    public class FeatureFlagResolverTests
    {
        // ── Precedence: present value wins, absent falls to default ────────────

        [Test]
        public void ResolveBool_ReturnsStoredValue_WhenPresent()
        {
            var values = new Dictionary<string, string> { { "pvp.enabled", "True" } };
            Assert.IsTrue(FeatureFlagResolver.ResolveBool(values, "pvp.enabled", fallback: false));
        }

        [Test]
        public void ResolveBool_ReturnsFallback_WhenKeyAbsent()
        {
            var values = new Dictionary<string, string>();
            Assert.IsTrue(FeatureFlagResolver.ResolveBool(values, "iap.enabled", fallback: true));
            Assert.IsFalse(FeatureFlagResolver.ResolveBool(values, "pvp.enabled", fallback: false));
        }

        [Test]
        public void ResolveBool_ReturnsFallback_WhenValueUnparseable()
        {
            var values = new Dictionary<string, string> { { "iap.enabled", "not-a-bool" } };
            Assert.IsTrue(FeatureFlagResolver.ResolveBool(values, "iap.enabled", fallback: true));
        }

        [Test]
        public void ResolveBool_ReturnsFallback_WhenDictionaryNull()
        {
            Assert.IsTrue(FeatureFlagResolver.ResolveBool(null, "iap.enabled", fallback: true));
        }

        // ── Typed parsing (invariant culture) ─────────────────────────────────

        [Test]
        public void ResolveFloat_ParsesInvariantCulture()
        {
            var values = new Dictionary<string, string> { { "idle.rate", "0.35" } };
            Assert.AreEqual(0.35f, FeatureFlagResolver.ResolveFloat(values, "idle.rate", 0.2f), 1e-6f);
        }

        [Test]
        public void ResolveInt_ParsesValue()
        {
            var values = new Dictionary<string, string> { { "idle.cap", "7200" } };
            Assert.AreEqual(7200, FeatureFlagResolver.ResolveInt(values, "idle.cap", 3600));
        }

        [Test]
        public void ResolveString_ReturnsRaw_OrFallback()
        {
            var values = new Dictionary<string, string> { { "api.url", "https://x" } };
            Assert.AreEqual("https://x", FeatureFlagResolver.ResolveString(values, "api.url", "default"));
            Assert.AreEqual("default", FeatureFlagResolver.ResolveString(values, "missing", "default"));
        }

        // ── Merge: remote overlays cache (remote wins) ────────────────────────

        [Test]
        public void MergeInto_RemoteOverridesCache_KeepsCacheOnlyKeys_DefaultsForAbsent()
        {
            // cache (last session): pvp on, iap on
            var merged = new Dictionary<string, string>
            {
                { "pvp.enabled", "True" },
                { "iap.enabled", "True" },
            };
            // remote (this session): pvp turned off; cloudsave key newly present
            var remote = new Dictionary<string, string>
            {
                { "pvp.enabled", "False" },
                { "cloudsave.enabled", "False" },
            };

            FeatureFlagResolver.MergeInto(merged, remote);

            // remote wins
            Assert.IsFalse(FeatureFlagResolver.ResolveBool(merged, "pvp.enabled", fallback: true));
            // cache-only key survives
            Assert.IsTrue(FeatureFlagResolver.ResolveBool(merged, "iap.enabled", fallback: false));
            // remote-only key applied
            Assert.IsFalse(FeatureFlagResolver.ResolveBool(merged, "cloudsave.enabled", fallback: true));
            // key in neither falls to compile-time default
            Assert.IsTrue(FeatureFlagResolver.ResolveBool(merged, "dailyevents.enabled", fallback: true));
        }

        // ── Registry sanity ───────────────────────────────────────────────────

        [Test]
        public void FeatureFlags_AllKeys_AreUniqueAndNonEmpty()
        {
            var seen = new HashSet<string>();
            foreach (var def in FeatureFlags.All)
            {
                Assert.IsFalse(string.IsNullOrEmpty(def.Key), "flag key must be non-empty");
                Assert.IsTrue(seen.Add(def.Key), $"duplicate flag key: {def.Key}");
            }
        }

        [Test]
        public void FeatureFlags_Accessors_ReturnDefaults_WhenServiceAbsent()
        {
            // No FeatureFlagService in an EditMode test → accessors must yield compile-time defaults.
            Assert.AreEqual(FeatureFlags.PvpEnabledDefault,         FeatureFlags.PvpEnabled);
            Assert.AreEqual(FeatureFlags.IapEnabledDefault,         FeatureFlags.IapEnabled);
            Assert.AreEqual(FeatureFlags.CloudSaveEnabledDefault,   FeatureFlags.CloudSaveEnabled);
            Assert.AreEqual(FeatureFlags.DailyEventsEnabledDefault, FeatureFlags.DailyEventsEnabled);
        }
    }
}
