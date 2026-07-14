using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Collects gameplay telemetry for economy balancing — discrete events (prestige, building
    /// placed, research, tier, megastructure) plus periodic full-state snapshots (net worth,
    /// income rate, building count, prestige scale) — and forwards them to an <see cref="IAnalyticsSink"/>.
    ///
    /// Collection is OFF by default and gated by the <c>analytics.enabled</c> Remote Config flag:
    /// flip it on to open a collection "phase", off to close it. Every event is stamped with the
    /// player id and the <c>analytics.phase</c> label so phases stay segmentable in the dashboard.
    ///
    /// Mirrors <see cref="FeatureFlagService"/>: self-bootstrapping singleton (no scene placement,
    /// so it can't be lost in a scene refactor), persists across scenes, never throws. Started from
    /// <see cref="SaveManager.ReconcileWithCloud"/> once UGS auth + flags are ready.
    /// </summary>
    [DefaultExecutionOrder(-80)]
    public class TelemetryService : SingletonMonoBehaviour<TelemetryService>
    {
        protected override bool PersistAcrossScenes => true;

        // 5 minutes. Long enough to be cheap, short enough to chart intra-run income scaling.
        const float SnapshotIntervalSeconds = 300f;

        // Cap on the pre-start buffer (see _preStart). Startup produces a handful of events at most;
        // a bound just stops a pathological launch (StartIfEnabled never reached) growing it forever.
        const int MaxPreStartEvents = 64;

        IAnalyticsSink _sink = new UnityAnalyticsSink();
        bool   _collecting;
        string _playerId = "";
        string _phase = "default";

        // False until StartIfEnabled (or DevForceStart) has resolved the analytics.enabled flag. This is
        // a THIRD state, distinct from "collecting" and "disabled": UGS auth + the Remote Config fetch take
        // seconds, and gameplay is already running — an offline research completion resolves in
        // ResearchService.Start() on the first frame. Treating "not yet decided" as "disabled" silently
        // dropped those events, so every offline research completion was invisible in the data.
        bool _startDecided;

        // Events recorded during that window. Held (never sent) until the decision lands: drained if
        // collection starts, discarded unsent if analytics turns out to be disabled — so buffering can
        // never leak data for a player who isn't being collected from.
        readonly List<(string name, IDictionary<string, object> p)> _preStart = new();

        // Session-cumulative count of manual field taps that collected an item (snapshot dimension).
        int    _fieldCollections;

        // Delivery counters. The sink swallows backend exceptions by contract, so without these a
        // failing pipeline is indistinguishable from a healthy one from inside the game (the bug that
        // made "analytics fire" report success while nothing reached the dashboard). Surfaced by DevStatus.
        int    _eventsSent;
        int    _eventsFailed;

        // Events accepted by the SDK but not yet handed to an upload. Boundary flushes are skipped when
        // this is zero (backgrounding a session that recorded nothing shouldn't hit the network), and it
        // collapses Android's focus-then-pause double-fire into a single upload. A failed flush leaves it
        // set, so the next boundary retries.
        int    _pendingEvents;

        // Run-length / playtime tracking (realtimeSinceStartup is monotonic and pause-independent).
        float _sessionStartTime;
        float _runStartTime;

        // NetWorth-delta baseline for the income-rate proxy (see CaptureSnapshot).
        bool  _haveNetWorthBaseline;
        float _lastNetWorth;
        float _lastNetWorthTime;

        // Self-bootstrap before any scene loads (same rationale as FeatureFlagService): no serialized
        // fields, so it needs no scene object and is created well ahead of SaveManager's StartIfEnabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            new GameObject(nameof(TelemetryService)).AddComponent<TelemetryService>();
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        /// <summary>
        /// Starts collection iff the <c>analytics.enabled</c> flag is true. No-op otherwise (the
        /// service stays fully inert — no sink call, no snapshot loop). Idempotent. Call after UGS
        /// init + sign-in and the Remote Config fetch (i.e. from SaveManager.ReconcileWithCloud).
        /// </summary>
        public void StartIfEnabled()
        {
            if (_collecting) return;

            if (!FeatureFlags.AnalyticsEnabled)
            {
                _startDecided = true;
                DiscardPreStart("analytics.enabled=false");
                GameLogger.Info("[Telemetry] Disabled (analytics.enabled=false) — not collecting this session.");
                return;
            }

            _phase    = string.IsNullOrEmpty(FeatureFlags.AnalyticsPhase) ? "default" : FeatureFlags.AnalyticsPhase;
            // SaveManager has already synced this to the UGS PlayerId during reconcile.
            _playerId = SaveManager.Instance?.Current?.playerId ?? "";

            bool started = _sink.StartCollection();
            // Collect even if the backend refused: the Record* calls then fail loudly and are counted,
            // which is diagnosable. Staying inert would reproduce the original silent-failure bug.
            _collecting   = true;
            _startDecided = true;

            _sessionStartTime = Time.realtimeSinceStartup;
            _runStartTime     = _sessionStartTime;

            StartCoroutine(SnapshotLoop());

            GameLogger.Info($"[Telemetry] Collection started (phase='{_phase}', player='{_playerId}'). {_sink.Describe()}");
            if (!started)
                GameLogger.Warning("[Telemetry] Backend refused StartCollection — events will NOT reach the dashboard.");

            // Anything gameplay produced before this point (offline research completion, and the update
            // notice when the reconcile overran ECSLoadBridge's timeout) is only now stampable — the
            // player id and phase did not exist until the lines above.
            DrainPreStart();
        }

        IEnumerator SnapshotLoop()
        {
            var wait = new WaitForSeconds(SnapshotIntervalSeconds);
            while (_collecting)
            {
                yield return wait;
                CaptureSnapshot();
            }
        }

        // UGS batches events in memory and uploads on its own cadence, so anything recorded since the
        // last upload sits in the buffer until the app is backgrounded, quits, or that cadence elapses.
        //
        // Both focus and pause are hooked because no single one of them covers every platform: Android
        // fires focus-loss *then* pause when the app is backgrounded, while the editor and standalone
        // desktop fire only focus-loss (OnApplicationPause depends on the Run In Background setting).
        // Hooking pause alone is why an event recorded in the editor never uploaded until a later launch.
        // FlushIfPending makes the resulting double-fire on Android harmless.
        void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) FlushIfPending("focus loss");
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) FlushIfPending("pause");
        }

        // Best-effort, and deliberately not the thing delivery rests on: Flush() hands the upload to the
        // SDK asynchronously, so the process can die before the request completes (exactly what happens
        // when you Stop play mode). The SDK persists its own buffer across sessions, so a truncated flush
        // delays the tail of a session to the next launch rather than losing it.
        void OnApplicationQuit() => FlushIfPending("quit");

        // ── Event API (called from gameplay taps; all no-op unless collecting) ─

        /// <summary>Prestige completed. Pass the pre-reset values captured in PrestigeSystem.</summary>
        public void RecordPrestige(long runCount, float netWorthBefore, long prestigeCurrencyEarned,
                                   int buildingCount, int highestTier)
        {
            if (!ShouldRecord) return;

            float now = Time.realtimeSinceStartup;
            var p = NewParams();
            p["run_count"]                = runCount;
            p["networth_before"]          = netWorthBefore;
            p["prestige_currency_earned"] = prestigeCurrencyEarned;
            p["playtime_run_sec"]         = (long)(now - _runStartTime);
            p["building_count"]           = buildingCount;
            p["highest_tier"]             = highestTier;
            Emit("prestige_completed", p);

            _runStartTime = now;     // the next run's clock starts at the prestige boundary
            CaptureSnapshot();       // bookend the run with a fresh state sample
        }

        /// <summary>A building was placed. Count is read live from ECS so the tap stays a one-liner.</summary>
        public void RecordBuildingPlaced(string buildingId)
        {
            if (!ShouldRecord) return;

            var p = NewParams();
            p["building_id"]          = buildingId ?? "";
            p["building_count_after"] = QueryBuildingCount();
            Emit("building_placed", p);
        }

        /// <summary>A research project completed. Cumulative entropy spent is read live from ECS.</summary>
        public void RecordResearch(string researchId)
        {
            if (!ShouldRecord) return;

            long entropySpent = TryReadEcs(out var progress, out _, out _) ? progress.TotalEntropySpent : 0L;
            var p = NewParams();
            p["research_id"]          = researchId ?? "";
            p["entropy_spent_total"]  = entropySpent;
            Emit("research_completed", p);
        }

        /// <summary>The player reached a new tier (absolute tier number).</summary>
        public void RecordTier(int tier)
        {
            if (!ShouldRecord) return;
            var p = NewParams();
            p["tier"] = tier;
            Emit("tier_reached", p);
        }

        /// <summary>A megastructure stage was completed (pass the new completed-stage count).</summary>
        public void RecordMegastructureStage(int stage)
        {
            if (!ShouldRecord) return;
            var p = NewParams();
            p["stage"] = stage;
            Emit("megastructure_stage", p);
        }

        /// <summary>
        /// The game-data update notice was shown to the player (a newer <c>gamedata.updatedUtc</c> stamp
        /// was published). <paramref name="dataVersion"/> is the low-cardinality <c>gamedata.version</c>
        /// label — use it to confirm a balance change's rollout reach in Data Explorer.
        /// </summary>
        public void RecordGameUpdateNotice(int dataVersion)
        {
            if (!ShouldRecord) return;
            var p = NewParams();
            p["data_version"] = dataVersion;
            Emit("game_update_notice", p);
        }

        /// <summary>
        /// Bumps the session field-collection counter (one manual field tap that yielded an item).
        /// Surfaced as the <c>field_collections</c> snapshot dimension — no per-tap event is emitted.
        /// </summary>
        public void NotifyFieldCollected()
        {
            if (!ShouldRecord) return;
            _fieldCollections++;
        }

        /// <summary>
        /// Emits a full player-state snapshot. Called on the 5-minute loop and at each prestige.
        /// <c>entropy_per_sec</c> is the NetWorth growth rate over the interval since the previous
        /// snapshot — a clean income-rate proxy because spending entropy moves BaseCurrency into
        /// TotalEntropySpent without changing NetWorth, so only actual production raises it.
        /// </summary>
        public void CaptureSnapshot()
        {
            if (!ShouldRecord) return;
            if (!TryReadEcs(out var progress, out var prestige, out int buildingCount)) return;

            float now      = Time.realtimeSinceStartup;
            float netWorth = progress.NetWorth;

            float entropyPerSec = 0f;
            if (_haveNetWorthBaseline)
            {
                float dt = now - _lastNetWorthTime;
                if (dt > 0.001f)
                    entropyPerSec = Mathf.Max(0f, (netWorth - _lastNetWorth) / dt);
            }
            _lastNetWorth         = netWorth;
            _lastNetWorthTime     = now;
            _haveNetWorthBaseline = true;

            var save = SaveManager.Instance?.Current;

            var p = NewParams();
            p["networth"]                = netWorth;
            p["base_currency"]           = progress.BaseCurrency;
            p["entropy_per_sec"]         = entropyPerSec;
            p["prestige_currency"]       = prestige.PrestigeCurrency;
            p["paid_currency"]           = save?.paidCurrency ?? 0L;
            p["prestige_count"]          = prestige.RunCount;
            p["building_count"]          = buildingCount;
            p["highest_tier"]            = progress.CurrentTier;
            p["megastructure_stage"]     = save?.megastructureStage ?? 0;
            p["research_unlocked_count"] = save?.unlockedResearch?.Count ?? 0;
            p["playtime_total_sec"]      = (long)(now - _sessionStartTime);
            p["field_collections"]       = _fieldCollections;
            p["field_cooldown_sec"]      = ManualFieldCollector.CurrentEffectiveFieldCooldown();
            ReadPowerNodeCounts(out int powerNodesTotal, out int powerNodesLinked);
            p["power_nodes_total"]       = powerNodesTotal;
            p["power_nodes_linked"]      = powerNodesLinked;
            Emit("player_snapshot", p);
        }

        /// <summary>
        /// Force-upload buffered events now (events are otherwise batched on an interval). Returns
        /// false if not collecting or the backend refused.
        /// </summary>
        public bool Flush()
        {
            if (!_collecting) return false;

            bool ok = _sink.Flush();
            if (ok) _pendingEvents = 0;
            else    GameLogger.Warning("[Telemetry] Flush failed — buffered events may be lost.");
            return ok;
        }

        /// <summary>
        /// Flushes only if something has been recorded since the last successful flush. This is what the
        /// lifecycle boundaries call: it keeps a quiet session from hitting the network, and it means the
        /// two boundaries Android fires back-to-back (focus loss, then pause) produce one upload, not two.
        /// </summary>
        bool FlushIfPending(string boundary)
        {
            if (!_collecting || _pendingEvents == 0) return false;

            int pending = _pendingEvents;
            bool ok = Flush();
            GameLogger.Debug($"[Telemetry] {boundary}: flushed {pending} pending event(s) — {(ok ? "ok" : "FAILED, will retry at the next boundary")}.");
            return ok;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// True while events are still worth constructing: we're collecting, or the flag decision is
        /// still pending and the event should be buffered rather than lost. False once we know analytics
        /// is disabled, which is the only state where a tap is genuinely a no-op.
        /// </summary>
        bool ShouldRecord => _collecting || !_startDecided;

        // Single funnel for every event: send, buffer, or drop — and count what actually reached the
        // backend so a broken pipeline is visible ('analytics status') instead of silently looking like
        // success. Values are captured by the caller at record time; the stamps are applied here, because
        // a buffered event's player id and phase are not known until collection starts.
        bool Emit(string eventName, IDictionary<string, object> p)
        {
            if (!_startDecided)
            {
                if (_preStart.Count < MaxPreStartEvents)
                {
                    _preStart.Add((eventName, p));
                    GameLogger.Debug($"[Telemetry] buffered '{eventName}' (collection not started yet)");
                }
                else
                {
                    GameLogger.Warning($"[Telemetry] pre-start buffer full — dropping '{eventName}'.");
                }
                return false;
            }

            if (!_collecting) return false;

            p["player_id"]        = _playerId;
            p["collection_phase"] = _phase;

            if (_sink.RecordEvent(eventName, p))
            {
                _eventsSent++;
                _pendingEvents++;
                GameLogger.Debug($"[Telemetry] sent '{eventName}'");
                return true;
            }

            _eventsFailed++;
            GameLogger.Warning($"[Telemetry] event '{eventName}' was NOT accepted by the backend.");
            return false;
        }

        // Sends everything recorded before collection started. Called once, from StartIfEnabled /
        // DevForceStart, after _startDecided is set — so the Emit calls below take the live path.
        void DrainPreStart()
        {
            if (_preStart.Count == 0) return;

            var buffered = _preStart.ToArray();
            _preStart.Clear();                    // cleared first: Emit must not re-buffer into it
            foreach (var (name, p) in buffered)
                Emit(name, p);

            GameLogger.Info($"[Telemetry] Drained {buffered.Length} event(s) recorded before collection started.");
        }

        // Analytics is off for this session, so the buffered events must never be sent.
        void DiscardPreStart(string reason)
        {
            if (_preStart.Count == 0) return;
            GameLogger.Info($"[Telemetry] Discarded {_preStart.Count} buffered event(s) unsent — {reason}.");
            _preStart.Clear();
        }

        // Stamps (player_id, collection_phase) are added in Emit, not here — they aren't known yet for
        // an event recorded during the startup window.
        IDictionary<string, object> NewParams() => new Dictionary<string, object>();

        // Reads the player's ECS singletons + building count. Returns false (and leaves outs at
        // default) if the world or singletons aren't ready yet — never throws.
        bool TryReadEcs(out PlayerProgressData progress, out PrestigeData prestige, out int buildingCount)
        {
            progress = default; prestige = default; buildingCount = 0;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return false;

            try
            {
                var em = world.EntityManager;
                using var progressQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerProgressData>());
                using var prestigeQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PrestigeData>());
                if (progressQuery.CalculateEntityCount() != 1 || prestigeQuery.CalculateEntityCount() != 1)
                    return false;

                progress      = progressQuery.GetSingleton<PlayerProgressData>();
                prestige      = prestigeQuery.GetSingleton<PrestigeData>();
                buildingCount = CountBuildings(em);
                return true;
            }
            catch (Exception ex)
            {
                GameLogger.Debug($"[Telemetry] ECS read skipped: {ex.Message}");
                return false;
            }
        }

        // Power grid connectivity: total placed power nodes vs how many chain back to a generator. The gap
        // (stranded relays) is the signal that link ranges are mistuned. Reads PowerGridSystem's singleton;
        // never throws — leaves both 0 if the grid state isn't available yet.
        void ReadPowerNodeCounts(out int total, out int linked)
        {
            total = 0; linked = 0;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            try
            {
                var em = world.EntityManager;
                using var q = em.CreateEntityQuery(ComponentType.ReadOnly<PowerGridState>());
                if (q.CalculateEntityCount() != 1) return;
                var state = q.GetSingleton<PowerGridState>();
                total  = state.TotalNodeCount;
                linked = state.LinkedNodeCount;
            }
            catch (Exception ex)
            {
                GameLogger.Debug($"[Telemetry] power-node read skipped: {ex.Message}");
            }
        }

        int QueryBuildingCount()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return 0;
            try { return CountBuildings(world.EntityManager); }
            catch { return 0; }
        }

        // Player-placed buildings only — exclude the permanent Entropy Sink fixture, matching how
        // PrestigeSystem treats it (WithNone<EntropySinkTag>).
        static int CountBuildings(EntityManager em)
        {
            using var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingData>(),
                ComponentType.Exclude<EntropySinkTag>());
            return q.CalculateEntityCount();
        }

        // ── Test seams ────────────────────────────────────────────────────────

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// Dev-only: force collection on with the live UGS sink, ignoring the analytics.enabled flag,
        /// so the dev console can smoke-test event delivery without configuring Remote Config. No-op if
        /// already collecting (e.g. the normal flag path already started it).
        /// </summary>
        public void DevForceStart()
        {
            if (_collecting)
            {
                // Not silent: an already-collecting service is the normal case when analytics.enabled
                // is on, and a silent early return here is why 'analytics fire' could log nothing at all.
                GameLogger.Info($"[Telemetry] DEV force-start skipped — already collecting (phase='{_phase}').");
                return;
            }

            _phase    = string.IsNullOrEmpty(FeatureFlags.AnalyticsPhase) ? "editor-test" : FeatureFlags.AnalyticsPhase;
            _playerId = SaveManager.Instance?.Current?.playerId ?? "";
            bool started = _sink.StartCollection();
            _collecting   = true;
            _startDecided = true;
            _sessionStartTime = Time.realtimeSinceStartup;
            _runStartTime     = _sessionStartTime;
            StartCoroutine(SnapshotLoop());

            GameLogger.Info($"[Telemetry] DEV force-start (phase='{_phase}'). {_sink.Describe()}");
            if (!started)
                GameLogger.Warning("[Telemetry] Backend refused StartCollection — events will NOT reach the dashboard.");

            DrainPreStart();
        }

        /// <summary>
        /// Dev-only: fire one of every event with sample data and flush immediately, then report what
        /// actually happened — how many the backend accepted, how many it refused, whether the flush
        /// succeeded, and the backend's own state. Deliberately reports counters rather than a canned
        /// success string: the previous version returned "Fired ..." even when every call had failed.
        /// </summary>
        public string DevFireAll()
        {
            DevForceStart();

            int sentBefore   = _eventsSent;
            int failedBefore = _eventsFailed;

            RecordBuildingPlaced("dev_test_building");
            RecordResearch("dev_test_research");
            RecordTier(3);
            RecordMegastructureStage(1);
            RecordGameUpdateNotice(FeatureFlags.GameDataVersion);
            // Sends prestige_completed AND an internal player_snapshot:
            RecordPrestige(runCount: 1, netWorthBefore: 12345f, prestigeCurrencyEarned: 42,
                           buildingCount: 7, highestTier: 3);
            CaptureSnapshot();   // explicit snapshot in case the prestige one couldn't read ECS

            bool flushed = Flush();

            int sent   = _eventsSent   - sentBefore;
            int failed = _eventsFailed - failedBefore;

            return $"Accepted by backend: {sent} event(s). Refused: {failed}. Flush: {(flushed ? "ok" : "FAILED")}.\n" +
                   $"{_sink.Describe()}\n" +
                   $"phase='{_phase}', player='{_playerId}'\n" +
                   "Note: 'accepted' only means the SDK took it. It still won't appear in the dashboard " +
                   "unless the event schema is registered in Event Manager for this environment.";
        }

        public string DevStatus()
        {
            string head;
            if (_collecting)
                head = $"collecting — phase='{_phase}', player='{_playerId}'";
            else if (!_startDecided)
                head = $"starting up — flag not resolved yet; {_preStart.Count} event(s) buffered, to be sent or discarded once it is.";
            else
                head = $"NOT collecting (analytics.enabled={FeatureFlags.AnalyticsEnabled}). 'analytics fire' will force-start.";

            return $"{head}\n{_sink.Describe()}\n" +
                   $"this session: {_eventsSent} event(s) accepted, {_eventsFailed} refused, " +
                   $"{_pendingEvents} awaiting upload";
        }
#endif

#if UNITY_EDITOR
        /// <summary>Inject a sink without starting collection (EditMode tests for the gating contract).</summary>
        internal void SetSinkForTesting(IAnalyticsSink sink) => _sink = sink;

        /// <summary>Force collection on with a recording sink, bypassing flags/UGS (EditMode tests).</summary>
        internal void StartForTesting(IAnalyticsSink sink, string phase = "test", string playerId = "test-player")
        {
            _sink             = sink;
            _phase            = phase;
            _playerId         = playerId;
            _collecting       = true;
            _startDecided     = true;
            _sessionStartTime = Time.realtimeSinceStartup;
            _runStartTime     = _sessionStartTime;
            DrainPreStart();
        }

        internal bool IsCollecting => _collecting;

        /// <summary>Events recorded before the analytics.enabled decision landed, still held in memory.</summary>
        internal int PreStartCount => _preStart.Count;
#endif
    }
}
