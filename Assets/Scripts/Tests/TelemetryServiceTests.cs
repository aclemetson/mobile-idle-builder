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
        public void Record_SendsNothingToSink_BeforeCollectionStarts()
        {
            var sink = new RecordingSink();
            _svc.SetSinkForTesting(sink);              // collection NOT started
            _svc.RecordTier(3);
            _svc.RecordMegastructureStage(1);
            Assert.IsEmpty(sink.Events, "nothing may reach the backend before the flag decision lands");
        }

        // ── Pre-start buffering ───────────────────────────────────────────────
        // UGS auth + the Remote Config fetch take seconds, and gameplay is already running: an offline
        // research completion resolves in ResearchService.Start() on the first frame, long before
        // StartIfEnabled. Treating "not decided yet" as "disabled" silently dropped every one of them.

        [Test]
        public void EventRecordedBeforeStart_IsBuffered_NotDropped()
        {
            var sink = new RecordingSink();
            _svc.SetSinkForTesting(sink);
            _svc.RecordResearch("recombination_i");    // offline completion, during the startup window

            Assert.IsEmpty(sink.Events, "must not reach the backend before the flag is resolved");
            Assert.AreEqual(1, _svc.PreStartCount, "the event must be held, not dropped");
        }

        [Test]
        public void BufferedEvent_IsDrainedAndStamped_WhenCollectionStarts()
        {
            var sink = new RecordingSink();
            _svc.SetSinkForTesting(sink);
            _svc.RecordResearch("recombination_i");    // buffered: no player id or phase exists yet

            _svc.StartForTesting(sink, phase: "phase-x", playerId: "pid-9");

            var p = sink.First("research_completed");
            Assert.IsNotNull(p, "the offline research completion must reach the backend once collection starts");
            Assert.AreEqual("recombination_i", p["research_id"], "the value captured at record time is preserved");
            Assert.AreEqual("pid-9",   p["player_id"],        "stamps are applied at drain, when they are first known");
            Assert.AreEqual("phase-x", p["collection_phase"]);
            Assert.AreEqual(0, _svc.PreStartCount, "the buffer is emptied by the drain");
        }

        [Test]
        public void BufferedEvents_AreDiscardedUnsent_WhenAnalyticsIsDisabled()
        {
            // analytics.enabled defaults false in an EditMode test (no FeatureFlagService).
            Assert.IsFalse(FeatureFlags.AnalyticsEnabled, "guard: default must be off");

            var sink = new RecordingSink();
            _svc.SetSinkForTesting(sink);
            _svc.RecordResearch("recombination_i");
            Assert.AreEqual(1, _svc.PreStartCount);

            _svc.StartIfEnabled();                     // resolves the flag: disabled

            Assert.IsEmpty(sink.Events, "buffering must never leak data for a player who isn't being collected from");
            Assert.AreEqual(0, _svc.PreStartCount, "the buffer is discarded, not left to grow");
        }

        [Test]
        public void Record_IsTrulyInert_OnceAnalyticsIsKnownDisabled()
        {
            var sink = new RecordingSink();
            _svc.SetSinkForTesting(sink);
            _svc.StartIfEnabled();                     // flag resolves to disabled
            Assert.IsFalse(_svc.IsCollecting);

            _svc.RecordTier(3);                        // taps after the decision must not buffer

            Assert.IsEmpty(sink.Events);
            Assert.AreEqual(0, _svc.PreStartCount, "a disabled session must not accumulate events in memory");
        }

        [Test]
        public void PreStartBuffer_IsBounded()
        {
            var sink = new RecordingSink();
            _svc.SetSinkForTesting(sink);

            for (int i = 0; i < 200; i++) _svc.RecordTier(i);

            Assert.AreEqual(64, _svc.PreStartCount,
                "a launch that never reaches StartIfEnabled must not grow the buffer without bound");
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
        public void OnApplicationFocus_Lost_FlushesBufferedEvents()
        {
            // The editor and standalone desktop fire focus-loss and NOT pause, so hooking pause alone
            // left editor-recorded events stranded in the SDK buffer until a later launch uploaded them.
            var sink = new RecordingSink();
            _svc.StartForTesting(sink, phase: "p", playerId: "u");
            _svc.RecordTier(4);

            InvokePrivate(_svc, "OnApplicationFocus", false);

            Assert.AreEqual(1, sink.FlushCount, "losing focus must flush buffered events");
        }

        [Test]
        public void OnApplicationFocus_Gained_DoesNotFlush()
        {
            var sink = new RecordingSink();
            _svc.StartForTesting(sink, phase: "p", playerId: "u");
            _svc.RecordTier(4);

            InvokePrivate(_svc, "OnApplicationFocus", true);

            Assert.AreEqual(0, sink.FlushCount, "regaining focus is not an upload boundary");
        }

        [Test]
        public void FocusLossThenPause_FlushesOnce()
        {
            // Android fires both when the app is backgrounded. The pending-event guard must collapse
            // them into a single upload rather than making a second, empty network call.
            var sink = new RecordingSink();
            _svc.StartForTesting(sink, phase: "p", playerId: "u");
            _svc.RecordTier(4);

            InvokePrivate(_svc, "OnApplicationFocus", false);
            InvokePrivate(_svc, "OnApplicationPause", true);

            Assert.AreEqual(1, sink.FlushCount, "the Android focus-then-pause pair must produce one upload");
        }

        [Test]
        public void Boundary_WithNothingRecorded_DoesNotFlush()
        {
            var sink = new RecordingSink();
            _svc.StartForTesting(sink, phase: "p", playerId: "u");   // collecting, but nothing recorded

            InvokePrivate(_svc, "OnApplicationFocus", false);
            InvokePrivate(_svc, "OnApplicationPause", true);
            InvokePrivate(_svc, "OnApplicationQuit");

            Assert.AreEqual(0, sink.FlushCount, "a session that recorded nothing must not hit the network");
        }

        [Test]
        public void FailedFlush_LeavesEventsPending_AndRetriesAtNextBoundary()
        {
            var sink = new RecordingSink();
            _svc.StartForTesting(sink, phase: "p", playerId: "u");
            _svc.RecordTier(4);

            sink.Accept = false;                                  // backend refuses the upload
            InvokePrivate(_svc, "OnApplicationFocus", false);
            Assert.AreEqual(1, sink.FlushCount, "the failing flush is still attempted");

            sink.Accept = true;                                   // backend recovers
            InvokePrivate(_svc, "OnApplicationPause", true);
            Assert.AreEqual(2, sink.FlushCount,
                "a failed flush must leave the events pending so the next boundary retries them");
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
