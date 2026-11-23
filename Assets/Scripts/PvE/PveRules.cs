using System;
using System.Collections.Generic;
using System.Globalization;

// Shared by the Unity client and the PvE Cloud Code module (CloudCode/PveModule links this file).
// Pure and deterministic: every time is passed in. Plain C# 9 only (no UnityEngine).
namespace MobileIdleBuilder
{
    /// <summary>Where a tournament week is. Entry: Fri 00:00-Sat 00:00 UTC (joining open). Running: Sat (last runs finish).
    /// Results: Sun 00:00 until the next Friday (settled, rewards claimable).</summary>
    public enum PvePhase { Entry, Running, Results }

    public enum SnapCheck { Accepted, Stale, NetWorthDropped, BadValue, RunOver }

    /// <summary>
    /// The PvE tournament rules (docs/agents/tasks/feature-pve-tournaments.md): the weekly schedule,
    /// snapshot acceptance and thinning, the "value at time t" comparison, ranking, promotion/demotion
    /// and reward bands. The server runs them for real; the client uses them for display only.
    /// </summary>
    public static class PveRules
    {
        public const string StartLeague = "bronze";
        public const int    DaySeconds = 86400;
        /// <summary>A snapshot sent up to this long after the 24h mark still counts (clamped to the mark).</summary>
        public const int    FinalGraceSeconds = 600;
        /// <summary>Net worth may fall this much between snapshots (rounding, a sold building) before a snapshot is refused.</summary>
        public const double MaxDropFraction = 0.01;
        public const int    MaxIdleCapSeconds = 43200;

        public static int RunSeconds(PveConfig cfg) => Math.Max(1, cfg?.run_hours ?? 24) * 3600;

        // ── Schedule ─────────────────────────────────────────────────────────

        /// <summary>The Friday 00:00 UTC that starts the tournament week containing <paramref name="nowUtc"/>.</summary>
        public static DateTime WeekStart(DateTime nowUtc)
        {
            var d = nowUtc.Date;
            int back = ((int)d.DayOfWeek - (int)DayOfWeek.Friday + 7) % 7;
            return DateTime.SpecifyKind(d.AddDays(-back), DateTimeKind.Utc);
        }

        public static string WeekId(DateTime nowUtc) => WeekStart(nowUtc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        public static DateTime WeekStartOf(string weekId) =>
            DateTime.SpecifyKind(DateTime.ParseExact(weekId, "yyyy-MM-dd", CultureInfo.InvariantCulture), DateTimeKind.Utc);

        public static PvePhase Phase(DateTime nowUtc)
        {
            double days = (nowUtc - WeekStart(nowUtc)).TotalDays;
            return days < 1 ? PvePhase.Entry : days < 2 ? PvePhase.Running : PvePhase.Results;
        }

        /// <summary>When the current phase ends.</summary>
        public static DateTime PhaseEnds(DateTime nowUtc)
        {
            var start = WeekStart(nowUtc);
            return Phase(nowUtc) switch
            {
                PvePhase.Entry   => start.AddDays(1),
                PvePhase.Running => start.AddDays(2),
                _                => start.AddDays(7),
            };
        }

        /// <summary>The next moment entry is open: now if it is open, else the next Friday 00:00.</summary>
        public static DateTime NextEntry(DateTime nowUtc) =>
            Phase(nowUtc) == PvePhase.Entry ? nowUtc : WeekStart(nowUtc).AddDays(7);

        public static bool CanJoin(DateTime nowUtc) => Phase(nowUtc) == PvePhase.Entry;

        /// <summary>
        /// When a week's brackets may be settled: Sunday 00:00 (every run has ended) plus the final-snapshot
        /// grace, so a run ending at midnight still gets its real last snapshot counted. The weekly schedule
        /// runs a little after this (Sunday 00:15 UTC).
        /// </summary>
        public static DateTime SettleAfter(string weekId) =>
            WeekStartOf(weekId).AddDays(2).AddSeconds(FinalGraceSeconds);

        /// <summary>Seconds since <paramref name="startUtc"/>, clamped to 0..<paramref name="runSeconds"/>.</summary>
        public static int Elapsed(DateTime startUtc, DateTime nowUtc, int runSeconds)
        {
            double s = (nowUtc - startUtc).TotalSeconds;
            if (s <= 0) return 0;
            return s >= runSeconds ? runSeconds : (int)s;
        }

        public static string Iso(DateTime utc) => utc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

        public static DateTime ParseIso(string iso) =>
            DateTime.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

        // ── Snapshots ────────────────────────────────────────────────────────

        /// <summary>
        /// Checks a new snapshot at raw elapsed time <paramref name="rawT"/> against the member's history and,
        /// when accepted, appends it (clamping t to the run). Within one bucket the newer snapshot replaces the
        /// older one, so a 24h run keeps at most 24h / bucket entries; t = 0 is always kept.
        /// </summary>
        public static SnapCheck AddSnap(List<PveSnap> snaps, int rawT, double nw, double rate, int cap,
                                        int runSeconds, int bucketSeconds)
        {
            if (double.IsNaN(nw) || double.IsInfinity(nw) || nw < 0) return SnapCheck.BadValue;
            if (double.IsNaN(rate) || double.IsInfinity(rate) || rate < 0) return SnapCheck.BadValue;
            if (rawT > runSeconds + FinalGraceSeconds) return SnapCheck.RunOver;
            int t = Math.Max(0, Math.Min(rawT, runSeconds));
            cap = Math.Max(0, Math.Min(cap, MaxIdleCapSeconds));

            var last = snaps.Count > 0 ? snaps[snaps.Count - 1] : null;
            if (last != null)
            {
                if (t < last.t) return SnapCheck.Stale;
                if (nw < last.nw * (1 - MaxDropFraction)) return SnapCheck.NetWorthDropped;
            }

            var snap = new PveSnap { t = t, nw = nw, rate = rate, cap = cap };
            int bucket = Math.Max(1, bucketSeconds);
            if (last != null && last.t > 0 && last.t / bucket == t / bucket && last.t < runSeconds)
                snaps[snaps.Count - 1] = snap;
            else if (last != null && last.t == t)
                snaps[snaps.Count - 1] = snap;
            else
                snaps.Add(snap);
            return SnapCheck.Accepted;
        }

        /// <summary>
        /// A member's net worth as of elapsed time <paramref name="t"/>: the last snapshot at or before t
        /// (real) when the member has played past t; otherwise their last snapshot plus idle earnings since
        /// (rate x time, capped at their idle cap), marked projected.
        /// </summary>
        public static (double value, bool projected) ValueAt(IReadOnlyList<PveSnap> snaps, int t)
        {
            if (snaps == null || snaps.Count == 0) return (0d, true);
            int i = snaps.Count - 1;
            while (i > 0 && snaps[i].t > t) i--;
            var s = snaps[i];
            if (s.t > t) return (0d, true);                   // the first snapshot is after t: nothing yet
            bool playedPast = i < snaps.Count - 1 || s.t == t;
            if (playedPast) return (s.nw, false);
            double idle = Math.Min(t - s.t, Math.Max(0, s.cap));
            return (s.nw + Math.Max(0, s.rate) * idle, true);
        }

        // ── Ranking and settlement ───────────────────────────────────────────

        public struct Ranked
        {
            public PveMember member;
            public double value;
            public bool projected;
            public int rank;          // 1-based
        }

        /// <summary>Members ranked by value at <paramref name="t"/>, highest first; ties go to the earlier start, then player id.</summary>
        public static List<Ranked> Rank(IEnumerable<PveMember> members, int t)
        {
            var list = new List<Ranked>();
            foreach (var m in members)
            {
                if (m == null) continue;
                var (v, p) = ValueAt(m.snaps, t);
                list.Add(new Ranked { member = m, value = v, projected = p });
            }
            list.Sort((a, b) =>
            {
                int c = b.value.CompareTo(a.value);
                if (c != 0) return c;
                c = string.CompareOrdinal(a.member.startUtc, b.member.startUtc);
                return c != 0 ? c : string.CompareOrdinal(a.member.playerId, b.member.playerId);
            });
            for (int i = 0; i < list.Count; i++) { var r = list[i]; r.rank = i + 1; list[i] = r; }
            return list;
        }

        public static PveLeagueDef League(PveConfig cfg, string id)
        {
            if (cfg?.leagues == null) return null;
            foreach (var l in cfg.leagues) if (l != null && l.id == id) return l;
            return null;
        }

        public static int LeagueIndex(PveConfig cfg, string id)
        {
            if (cfg?.leagues == null) return -1;
            for (int i = 0; i < cfg.leagues.Length; i++) if (cfg.leagues[i]?.id == id) return i;
            return -1;
        }

        /// <summary>Bracket capacity for a league (the test size while small brackets are on).</summary>
        public static int Capacity(PveConfig cfg, PveLeagueDef league, bool smallBrackets) =>
            smallBrackets ? Math.Max(2, cfg?.test_bracket_size ?? 5) : Math.Max(2, league?.bracket_size ?? 100);

        /// <summary>
        /// How many of a bracket of <paramref name="size"/> go up and down. A bracket below its capacity scales
        /// both down (rounded down, at least 1 when the league moves anyone and the bracket has 3+ members);
        /// the top league promotes nobody and the bottom demotes nobody; promote + demote never exceeds size.
        /// </summary>
        public static (int promote, int demote) Moves(PveConfig cfg, PveLeagueDef league, int size, int capacity)
        {
            if (league == null || size <= 1) return (0, 0);
            int idx = LeagueIndex(cfg, league.id);
            bool top    = idx == (cfg?.leagues?.Length ?? 1) - 1;
            bool bottom = idx == 0;
            int Scale(int n)
            {
                if (n <= 0) return 0;
                if (size >= capacity || capacity <= 0) return n;
                int s = (int)Math.Floor((double)n * size / capacity);
                return size >= 3 ? Math.Max(1, s) : s;
            }
            int up   = top ? 0 : Scale(league.promote);
            int down = bottom ? 0 : Scale(league.demote);
            if (up + down > size) down = Math.Max(0, size - up);
            return (up, down);
        }

        /// <summary>The league a member moves to, and "promoted" / "demoted" / "stayed".</summary>
        public static (string league, string result) NextLeague(PveConfig cfg, string leagueId, int rank, int size, int promote, int demote)
        {
            int idx = LeagueIndex(cfg, leagueId);
            if (idx < 0) return (leagueId, "stayed");
            if (rank <= promote && idx + 1 < cfg.leagues.Length) return (cfg.leagues[idx + 1].id, "promoted");
            if (rank > size - demote && idx > 0) return (cfg.leagues[idx - 1].id, "demoted");
            return (leagueId, "stayed");
        }

        /// <summary>The first reward band <paramref name="rank"/> (of <paramref name="size"/>) falls in, or null.</summary>
        public static PveRewardBand RewardFor(PveLeagueDef league, int rank, int size)
        {
            if (league?.rewards == null) return null;
            foreach (var b in league.rewards)
            {
                if (b == null) continue;
                if (b.rank_to > 0) { if (rank <= b.rank_to) return b; continue; }
                if (b.top_percent > 0)
                {
                    int cut = (int)Math.Ceiling(size * b.top_percent / 100.0);
                    if (rank <= cut) return b;
                    continue;
                }
                return b;   // catch-all band
            }
            return null;
        }

        // ── Ids ──────────────────────────────────────────────────────────────

        public static string WeekIndexId(string week) => $"pve_{week}";
        public static string CursorKey(string league, bool dev) => dev ? league + "_dev" : league;
        public static string BracketId(string week, string league, bool dev, int n) =>
            $"pve_{week}_{CursorKey(league, dev)}_{n}";
        public static string MemberKey(string playerId) => "m_" + playerId;
        public const string MetaKey = "meta";
        public const string IndexKey = "index";
        public const string ProfileKey = "pve_profile";
        public const string SettingsId = "pve_settings";
        public const string SettingsKey = "settings";
    }

    /// <summary>Server-side switches, kept in the private custom item pve_settings/settings (editable in the dashboard).</summary>
    [Serializable]
    public class PveSettings
    {
        public bool smallBrackets;      // use game_config.pve.test_bracket_size for every bracket
        public bool allowDebugTime;     // honour the client's debugNow (testing only; never in production)
    }
}
