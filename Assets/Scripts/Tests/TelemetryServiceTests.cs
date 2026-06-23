using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Verifies TelemetryService event construction against a recording sink: the gating contract
    /// (no events unless collecting), correct event names, and that every event carries the right
    /// params plus the player-id / collection-phase stamps. Uses the StartForTesting seam so no UGS,
    /// network, or ECS world is required.
    /// </summary>
    [TestFixture]
    public class TelemetryServiceTests
    {
        class RecordingSink : IAnalyticsSink
        {
            public int StartCount;
            public readonly List<(string name, IDictionary<string, object> p)> Events = new();

            public void StartCollection() => StartCount++;

            public void RecordEvent(string eventName, IDictionary<string, object> parameters)
                => Events.Add((eventName, new Dictionary<string, object>(parameters)));

            public IDictionary<string, object> First(string name) =>
                Events.Find(e => e.name == name).p;
        }

        TelemetryService _svc;

        [SetUp]
        public void SetUp()
        {
            _svc = new GameObject(nameof(TelemetryService)).AddComponent<TelemetryService>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_svc != null) Object.DestroyImmediate(_svc.gameObject);
        }

        // ── Gating ────────────────────────────────────────────────────────────

        [Test]
        public void Record_IsNoOp_WhenNotCollecting()
        {
            var sink = new RecordingSink();
            _svc.SetSinkForTesting(sink);              // collection NOT started
            _svc.RecordTier(3);
            _svc.RecordMegastructureStage(1);
            Assert.IsEmpty(sink.Events, "no events should be recorded while collection is off");
        }

        [Test]
        public void StartIfEnabled_DoesNotCollect_WhenFlagDefaultsOff()
        {
            // No FeatureFlagService in an EditMode test → analytics.enabled resolves to its
            // compile-time default (false), so collection must stay off.
            Assert.IsFalse(FeatureFlags.AnalyticsEnabled, "guard: default must be off");
            _svc.StartIfEnabled();
            Assert.IsFalse(_svc.IsCollecting);
        }

        // ── Event construction ────────────────────────────────────────────────

        [Test]
        public void RecordPrestige_EmitsEventWithPreResetParamsAndStamps()
        {
            var sink = new RecordingSink();
            _svc.StartForTesting(sink, phase: "prestige-tuning-v1", playerId: "pid-123");

            _svc.RecordPrestige(runCount: 5, netWorthBefore: 1000f, prestigeCurrencyEarned: 50,
                                buildingCount: 12, highestTier: 4);

            var p = sink.First("prestige_completed");
            Assert.IsNotNull(p, "prestige_completed event must be recorded");
            Assert.AreEqual(5L,    p["run_count"]);
            Assert.AreEqual(1000f, (float)p["networth_before"], 1e-3f);
            Assert.AreEqual(50L,   p["prestige_currency_earned"]);
            Assert.AreEqual(12,    p["building_count"]);
            Assert.AreEqual(4,     p["highest_tier"]);
            Assert.IsTrue(p.ContainsKey("playtime_run_sec"));
            // Stamps present on every event
            Assert.AreEqual("pid-123",            p["player_id"]);
            Assert.AreEqual("prestige-tuning-v1", p["collection_phase"]);
        }

        [Test]
        public void RecordTier_EmitsTierEvent()
        {
            var sink = new RecordingSink();
            _svc.StartForTesting(sink, phase: "p", playerId: "u");
            _svc.RecordTier(7);

            var p = sink.First("tier_reached");
            Assert.IsNotNull(p);
            Assert.AreEqual(7, p["tier"]);
            Assert.AreEqual("p", p["collection_phase"]);
        }

        [Test]
        public void RecordMegastructureStage_EmitsStageEvent()
        {
            var sink = new RecordingSink();
            _svc.StartForTesting(sink, phase: "p", playerId: "u");
            _svc.RecordMegastructureStage(3);

            var p = sink.First("megastructure_stage");
            Assert.IsNotNull(p);
            Assert.AreEqual(3, p["stage"]);
        }
    }
}
