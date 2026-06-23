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

        IAnalyticsSink _sink = new UnityAnalyticsSink();
        bool   _collecting;
        string _playerId = "";
        string _phase = "default";

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
                GameLogger.Info("[Telemetry] Disabled (analytics.enabled=false) — not collecting this session.");
                return;
            }

            _phase    = string.IsNullOrEmpty(FeatureFlags.AnalyticsPhase) ? "default" : FeatureFlags.AnalyticsPhase;
            // SaveManager has already synced this to the UGS PlayerId during reconcile.
            _playerId = SaveManager.Instance?.Current?.playerId ?? "";

            _sink.StartCollection();
            _collecting = true;

            _sessionStartTime = Time.realtimeSinceStartup;
            _runStartTime     = _sessionStartTime;

            StartCoroutine(SnapshotLoop());
            GameLogger.Info($"[Telemetry] Collection started (phase='{_phase}', player='{_playerId}').");
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

        // ── Event API (called from gameplay taps; all no-op unless collecting) ─

        /// <summary>Prestige completed. Pass the pre-reset values captured in PrestigeSystem.</summary>
        public void RecordPrestige(long runCount, float netWorthBefore, long prestigeCurrencyEarned,
                                   int buildingCount, int highestTier)
        {
            if (!_collecting) return;

            float now = Time.realtimeSinceStartup;
            var p = NewParams();
            p["run_count"]                = runCount;
            p["networth_before"]          = netWorthBefore;
            p["prestige_currency_earned"] = prestigeCurrencyEarned;
            p["playtime_run_sec"]         = (long)(now - _runStartTime);
            p["building_count"]           = buildingCount;
            p["highest_tier"]             = highestTier;
            _sink.RecordEvent("prestige_completed", p);

            _runStartTime = now;     // the next run's clock starts at the prestige boundary
            CaptureSnapshot();       // bookend the run with a fresh state sample
        }

        /// <summary>A building was placed. Count is read live from ECS so the tap stays a one-liner.</summary>
        public void RecordBuildingPlaced(string buildingId)
        {
            if (!_collecting) return;

            var p = NewParams();
            p["building_id"]          = buildingId ?? "";
            p["building_count_after"] = QueryBuildingCount();
            _sink.RecordEvent("building_placed", p);
        }

        /// <summary>A research project completed. Cumulative entropy spent is read live from ECS.</summary>
        public void RecordResearch(string researchId)
        {
            if (!_collecting) return;

            long entropySpent = TryReadEcs(out var progress, out _, out _) ? progress.TotalEntropySpent : 0L;
            var p = NewParams();
            p["research_id"]          = researchId ?? "";
            p["entropy_spent_total"]  = entropySpent;
            _sink.RecordEvent("research_completed", p);
        }

        /// <summary>The player reached a new tier (absolute tier number).</summary>
        public void RecordTier(int tier)
        {
            if (!_collecting) return;
            var p = NewParams();
            p["tier"] = tier;
            _sink.RecordEvent("tier_reached", p);
        }

        /// <summary>A megastructure stage was completed (pass the new completed-stage count).</summary>
        public void RecordMegastructureStage(int stage)
        {
            if (!_collecting) return;
            var p = NewParams();
            p["stage"] = stage;
            _sink.RecordEvent("megastructure_stage", p);
        }

        /// <summary>
        /// Emits a full player-state snapshot. Called on the 5-minute loop and at each prestige.
        /// <c>entropy_per_sec</c> is the NetWorth growth rate over the interval since the previous
        /// snapshot — a clean income-rate proxy because spending entropy moves BaseCurrency into
        /// TotalEntropySpent without changing NetWorth, so only actual production raises it.
        /// </summary>
        public void CaptureSnapshot()
        {
            if (!_collecting) return;
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
            _sink.RecordEvent("player_snapshot", p);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        IDictionary<string, object> NewParams() => new Dictionary<string, object>
        {
            { "player_id",        _playerId },
            { "collection_phase", _phase },
        };

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
            _sessionStartTime = Time.realtimeSinceStartup;
            _runStartTime     = _sessionStartTime;
        }

        internal bool IsCollecting => _collecting;
#endif
    }
}
