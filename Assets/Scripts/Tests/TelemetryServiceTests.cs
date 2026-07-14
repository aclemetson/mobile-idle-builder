using System.Collections.Generic;
using System.Reflection;
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
            public int FlushCount;

            /// <summary>Set false to simulate a backend that refuses everything (UGS down, not signed in).</summary>
            public bool Accept = true;

            public readonly List<(string name, IDictionary<string, object> p)> Events = new();

            public bool StartCollection()
            {
                StartCount++;
                return Accept;
            }

            public bool RecordEvent(string eventName, IDictionary<string, object> parameters)
            {
                if (!Accept) return false;
                Events.Add((eventName, new Dictionary<string, object>(parameters)));
                return true;
            }

            public bool Flush()
            {
                FlushCount++;
                return Accept;
            }

            public string Describe() => "recording sink (test)";

            public IDictionary<string, object> First(string name) =>
                Events.Find(e => e.name == name).p;
        }

        // Unity's lifecycle callbacks are private by convention; drive them the way the engine would.
        static void InvokePrivate(object target, string method, params object[] args) =>
            target.GetType()
                  .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                  .Invoke(target, args);

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

        [Test]
        public void DevForceStart_StartsCollection_AndFlushPassesThrough()
        {
            var sink = new RecordingSink();
            _svc.SetSinkForTesting(sink);          // inject before starting
            _svc.DevForceStart();                   // dev path: starts without the flag
            Assert.IsTrue(_svc.IsCollecting);
            Assert.AreEqual(1, sink.StartCount);

            _svc.RecordTier(2);                     // events now flow
            Assert.IsNotNull(sink.First("tier_reached"));

            Assert.IsTrue(_svc.Flush());
            Assert.AreEqual(1, sink.FlushCount);
        }

        // ── Delivery: flush on background / quit ──────────────────────────────
        // UGS batches events in memory, so an event recorded seconds before the OS kills a
        // backgrounded app never uploads unless we flush at the lifecycle boundary.

        [Test]
        public void OnApplicationPause_Backgrounded_FlushesBufferedEvents()
        {
            var sink = new RecordingSink();
            _svc.StartForTesting(sink, phase: "p", playerId: "u");
            _svc.RecordTier(4);

            InvokePrivate(_svc, "OnApplicationPause", true);

            Assert.AreEqual(1, sink.FlushCount, "backgrounding must flush buffered events");
        }

        [Test]
        public void OnApplicationPause_Resumed_DoesNotFlush()
        {
            var sink = new RecordingSink();
            _svc.StartForTesting(sink, phase: "p", playerId: "u");

            InvokePrivate(_svc, "OnApplicationPause", false);

            Assert.AreEqual(0, sink.FlushCount, "returning to the foreground is not an upload boundary");
        }

        [Test]
        public void OnApplicationQuit_FlushesBufferedEvents()
        {
            var sink = new RecordingSink();
            _svc.StartForTesting(sink, phase: "p", playerId: "u");
            _svc.RecordTier(4);

            InvokePrivate(_svc, "OnApplicationQuit");

            Assert.AreEqual(1, sink.FlushCount);
        }

        [Test]
        public void Flush_WhenNotCollecting_ReturnsFalse_AndDoesNotTouchSink()
        {
            var sink = new RecordingSink();
            _svc.SetSinkForTesting(sink);          // collection NOT started

            Assert.IsFalse(_svc.Flush());
            Assert.AreEqual(0, sink.FlushCount);
        }

        // ── Delivery: a refusing backend must be visible, not silent ──────────

        [Test]
        public void DevStatus_ReportsRefusedEvents_WhenBackendRejectsThem()
        {
            var sink = new RecordingSink { Accept = false };   // UGS down / not signed in
            _svc.StartForTesting(sink, phase: "p", playerId: "u");

            _svc.RecordTier(1);
            _svc.RecordResearch("r");

            Assert.IsEmpty(sink.Events, "a refusing backend records nothing");

            string status = _svc.DevStatus();
            StringAssert.Contains("0 event(s) accepted", status);
            StringAssert.Contains("2 refused", status,
                "a broken pipeline must be visible in 'analytics status', not silently look healthy");
        }
    }
}
