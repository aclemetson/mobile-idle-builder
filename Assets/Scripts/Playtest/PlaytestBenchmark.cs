#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MobileIdleBuilder.Playtest
{
    /// <summary>
    /// Records when a playtest run reaches each milestone, in player time: the seconds a real
    /// player would have spent (game time the driver let run, plus simulated absences), not the
    /// agent's wall-clock time. Milestones are detected by diffing a small probe of game state
    /// once per real second, and appended to a JSONL file as they happen so a crashed run still
    /// leaves its timings. See Playtests/README.md, "Benchmarks".
    /// </summary>
    public sealed class PlaytestBenchmark
    {
        private const float SampleIntervalSeconds = 1f;

        public static PlaytestBenchmark Active { get; private set; }

        public string Scenario { get; }
        public string RunId { get; }
        public string EventsPath { get; }
        public double PlayerSeconds { get; private set; }
        public int    Actions { get; private set; }
        public double DollarsSpent { get; private set; }
        public long   CrystalsBought { get; private set; }

        private readonly float _realStart;
        private readonly List<Milestone> _milestones = new();
        private readonly HashSet<string> _seen = new();
        private Probe _last;
        private float _nextSample;

        [Serializable]
        public sealed class Milestone
        {
            public string type;
            public string detail;
            public double playerSeconds;
            public double realSeconds;
            public int    actions;
            public double dollars;
        }

        /// <summary>The slice of game state milestones are detected from.</summary>
        public sealed class Probe
        {
            public bool   loaded;
            public string tutorialStep;
            public bool   tutorialActive;
            public HashSet<string> research = new();
            public string activeResearch;
            public Dictionary<string, int> buildings = new();
            public int    buildingTotal;
            public long   entropy;
            public float  netWorth;
            public int    tier;
            public bool   prestigeAvailable;
            public int    prestigeRuns;
            public int    sitesUnlocked;
            public int    worldsUnlocked;
            public long   crystals;
        }

        private PlaytestBenchmark(string scenario, string runId, string eventsPath)
        {
            Scenario   = scenario;
            RunId      = runId;
            EventsPath = eventsPath;
            _realStart = Time.realtimeSinceStartup;
        }

        /// <summary>Starts a run; replaces any active one. The first probe is the baseline.</summary>
        public static PlaytestBenchmark Start(string scenario, string runId, string directory)
        {
            Directory.CreateDirectory(directory);
            var bench = new PlaytestBenchmark(scenario, runId, Path.Combine(directory, runId + ".jsonl"));
            Active = bench;
            bench._last = CaptureProbe();
            bench.Record("run_start", scenario);
            return bench;
        }

        public static void Stop() => Active = null;

        // ── Inputs from the driver and tools ──────────────────────────────────

        /// <summary>Advances player time by one frame's worth and samples state once a second.</summary>
        public void Tick(double playerDelta)
        {
            PlayerSeconds += playerDelta;
            if (Time.realtimeSinceStartup < _nextSample) return;
            _nextSample = Time.realtimeSinceStartup + SampleIntervalSeconds;
            Sample();
        }

        public void CountAction() => Actions++;

        public void AddAway(double seconds)
        {
            PlayerSeconds += seconds;
            Record("away", FormatDuration(seconds));
        }

        public void AddPurchase(double dollars, long crystals, string pack)
        {
            DollarsSpent   += dollars;
            CrystalsBought += crystals;
            Record("purchase", $"{pack}: ${dollars:0.00} for {crystals} crystals");
        }

        /// <summary>Samples immediately; call before reading or ending so nothing is missed.</summary>
        public void Sample()
        {
            var probe = CaptureProbe();
            foreach (var (type, detail) in Detect(_last, probe, _seen))
                Record(type, detail);
            if (probe.loaded) _last = probe;
        }

        public void Record(string type, string detail)
        {
            var m = new Milestone
            {
                type          = type,
                detail        = detail,
                playerSeconds = Math.Round(PlayerSeconds, 1),
                realSeconds   = Math.Round(Time.realtimeSinceStartup - _realStart, 1),
                actions       = Actions,
                dollars       = DollarsSpent,
            };
            _milestones.Add(m);
            try { File.AppendAllText(EventsPath, JsonUtility.ToJson(m) + "\n"); }
            catch (Exception e) { GameLogger.Warning($"[Playtest] Could not write benchmark event: {e.Message}"); }
        }

        public IReadOnlyList<Milestone> Milestones => _milestones;

        // ── Detection (pure; covered by PlaytestBenchmarkTests) ───────────────

        private static readonly int[] BuildingCountSteps = { 1, 5, 10, 25, 50, 100, 200 };

        /// <summary>
        /// Milestones between two probes. One-time milestones are keyed in <paramref name="seen"/>
        /// so they fire once per run even if a value dips and recovers (entropy is spent).
        /// </summary>
        public static List<(string type, string detail)> Detect(Probe prev, Probe cur, HashSet<string> seen)
        {
            var events = new List<(string, string)>();
            if (cur == null || !cur.loaded) return events;
            prev ??= new Probe();

            void Once(string type, string detail)
            {
                if (seen.Add(type + ":" + detail)) events.Add((type, detail));
            }

            if (!string.IsNullOrEmpty(cur.tutorialStep) && cur.tutorialStep != prev.tutorialStep && cur.tutorialActive)
                Once("tutorial_step", cur.tutorialStep);
            if (prev.loaded && prev.tutorialActive && !cur.tutorialActive)
                Once("tutorial_complete", prev.tutorialStep ?? "");

            if (!string.IsNullOrEmpty(cur.activeResearch) && cur.activeResearch != prev.activeResearch)
                Once("research_started", cur.activeResearch);
            foreach (var id in cur.research)
                if (!prev.research.Contains(id)) Once("research_done", id);

            foreach (var kv in cur.buildings)
                if (kv.Value > 0 && (!prev.buildings.TryGetValue(kv.Key, out int before) || before == 0))
                    Once("building_first", kv.Key);
            foreach (int step in BuildingCountSteps)
                if (cur.buildingTotal >= step && prev.buildingTotal < step) Once("buildings_total", step.ToString());

            for (int k = 2; k <= 15; k++)
            {
                double threshold = Math.Pow(10, k);
                if (cur.entropy >= threshold)  Once("entropy", $"1e{k}");
                if (cur.netWorth >= threshold) Once("net_worth", $"1e{k}");
            }

            if (cur.tier > prev.tier && cur.tier > 0) Once("tier", cur.tier.ToString());
            if (cur.prestigeAvailable) Once("prestige_available", "");
            if (cur.prestigeRuns > prev.prestigeRuns) Once("prestige", cur.prestigeRuns.ToString());
            if (cur.sitesUnlocked > prev.sitesUnlocked && prev.loaded) Once("site_unlocked", cur.sitesUnlocked.ToString());
            if (cur.worldsUnlocked > prev.worldsUnlocked && prev.loaded) Once("world_unlocked", cur.worldsUnlocked.ToString());

            // Not one-time: every crystal drop between samples is spending worth seeing.
            if (prev.loaded && cur.crystals < prev.crystals)
                events.Add(("crystals_spent", (prev.crystals - cur.crystals).ToString()));
            return events;
        }

        public static Probe CaptureProbe()
        {
            var s     = PlaytestState.Capture();
            var save  = SaveManager.Instance?.Current;
            var probe = new Probe { loaded = s.loaded && s.economy != null };
            if (!probe.loaded) return probe;

            probe.tutorialStep      = s.tutorial?.stepId;
            probe.tutorialActive    = s.tutorial?.active ?? false;
            probe.activeResearch    = s.research?.activeId;
            if (s.research != null) foreach (var id in s.research.unlocked) probe.research.Add(id);
            if (s.grid != null)
            {
                probe.buildingTotal = s.grid.buildingCount;
                foreach (var b in s.grid.buildings)
                    probe.buildings[b.type] = probe.buildings.TryGetValue(b.type, out int n) ? n + 1 : 1;
            }
            probe.entropy           = s.economy.entropy;
            probe.netWorth          = s.economy.netWorth;
            probe.tier              = s.economy.tier;
            probe.prestigeAvailable = s.economy.prestigeAvailable;
            probe.prestigeRuns      = s.economy.prestigeRuns;
            probe.crystals          = s.economy.crystals;
            probe.sitesUnlocked     = save?.unlockedSites?.Count ?? 0;
            probe.worldsUnlocked    = save?.unlockedWorlds?.Count ?? 0;
            return probe;
        }

        public static string FormatDuration(double seconds)
        {
            var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}m" : $"{t.Minutes}m {t.Seconds:00}s";
        }
    }
}
#endif
