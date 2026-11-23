using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// PvE phase 3: the shared tournament rules (also compiled into the Cloud Code module), the imported
    /// league config, JSON compatibility of the shared models, and the client API wrapper.
    /// </summary>
    public class PveRulesTests
    {
        static DateTime Utc(int y, int mo, int d, int h = 0, int mi = 0, int s = 0) =>
            new DateTime(y, mo, d, h, mi, s, DateTimeKind.Utc);

        // 2026-11-27 is a Friday.
        static readonly DateTime Fri = Utc(2026, 11, 27);

        static PveConfig RealConfig()
        {
            var cfg = AssetDatabase.LoadAssetAtPath<GameConfigSO>("Assets/Data/settings/game_config.asset");
            Assert.IsNotNull(cfg?.pve?.leagues, "game_config.pve not imported");
            return cfg.pve;
        }

        static PveConfig Ladder(params (string id, int size, int up, int down)[] leagues) => new PveConfig
        {
            leagues = leagues.Select(l => new PveLeagueDef { id = l.id, name = l.id, bracket_size = l.size, promote = l.up, demote = l.down }).ToArray(),
        };

        // ── Schedule ──────────────────────────────────────────────────────────

        [Test]
        public void WeekStart_IsTheFridayMidnightAtOrBefore()
        {
            Assert.AreEqual(Fri, PveRules.WeekStart(Fri));
            Assert.AreEqual(Fri, PveRules.WeekStart(Fri.AddHours(13)));
            Assert.AreEqual(Fri, PveRules.WeekStart(Fri.AddDays(6).AddHours(23).AddMinutes(59)));   // Thursday night
            Assert.AreEqual(Fri.AddDays(-7), PveRules.WeekStart(Fri.AddSeconds(-1)));
            Assert.AreEqual("2026-11-27", PveRules.WeekId(Fri.AddDays(3)));
            Assert.AreEqual(Fri, PveRules.WeekStartOf("2026-11-27"));
        }

        [Test]
        public void Phases_FridayEntry_SaturdayRunning_SundayOnResults()
        {
            Assert.AreEqual(PvePhase.Entry,   PveRules.Phase(Fri));
            Assert.AreEqual(PvePhase.Entry,   PveRules.Phase(Fri.AddHours(23).AddMinutes(59)));
            Assert.AreEqual(PvePhase.Running, PveRules.Phase(Fri.AddDays(1)));
            Assert.AreEqual(PvePhase.Results, PveRules.Phase(Fri.AddDays(2)));
            Assert.AreEqual(PvePhase.Results, PveRules.Phase(Fri.AddDays(6)));
            Assert.IsTrue(PveRules.CanJoin(Fri.AddHours(5)));
            Assert.IsFalse(PveRules.CanJoin(Fri.AddDays(1)));
            Assert.AreEqual(Fri.AddDays(1), PveRules.PhaseEnds(Fri.AddHours(3)));
            Assert.AreEqual(Fri.AddDays(7), PveRules.NextEntry(Fri.AddDays(3)));
            Assert.AreEqual(Fri.AddDays(2).AddMinutes(10), PveRules.SettleAfter("2026-11-27"),
                "settlement waits for the final-snapshot grace after the last run ends");
        }

        [Test]
        public void LatestJoin_StillFinishesBeforeSettlement()
        {
            var lastJoin = Fri.AddDays(1).AddSeconds(-1);
            Assert.IsTrue(PveRules.CanJoin(lastJoin));
            Assert.LessOrEqual(lastJoin.AddSeconds(PveRules.DaySeconds + PveRules.FinalGraceSeconds),
                               PveRules.SettleAfter(PveRules.WeekId(lastJoin)),
                "the latest run's final snapshot (with grace) lands before settlement");
        }

        [Test]
        public void Elapsed_IsClampedToTheRun()
        {
            Assert.AreEqual(0, PveRules.Elapsed(Fri, Fri.AddSeconds(-5), 86400));
            Assert.AreEqual(3600, PveRules.Elapsed(Fri, Fri.AddHours(1), 86400));
            Assert.AreEqual(86400, PveRules.Elapsed(Fri, Fri.AddHours(30), 86400));
        }

        // ── Snapshots ─────────────────────────────────────────────────────────

        const int Run = 86400, Bucket = 600;

        static List<PveSnap> Start() => new List<PveSnap> { new PveSnap { t = 0 } };

        [Test]
        public void AddSnap_AcceptsGrowth_RefusesStaleDropsAndBadValues()
        {
            var s = Start();
            Assert.AreEqual(SnapCheck.Accepted, PveRules.AddSnap(s, 700, 1000, 2, 1800, Run, Bucket));
            Assert.AreEqual(SnapCheck.Stale, PveRules.AddSnap(s, 650, 1100, 2, 1800, Run, Bucket));
            Assert.AreEqual(SnapCheck.Accepted, PveRules.AddSnap(s, 1300, 995, 2, 1800, Run, Bucket), "a 0.5% dip is allowed");
            Assert.AreEqual(SnapCheck.NetWorthDropped, PveRules.AddSnap(s, 1900, 900, 2, 1800, Run, Bucket));
            Assert.AreEqual(SnapCheck.BadValue, PveRules.AddSnap(s, 1900, double.NaN, 2, 1800, Run, Bucket));
            Assert.AreEqual(SnapCheck.BadValue, PveRules.AddSnap(s, 1900, 2000, -1, 1800, Run, Bucket));
            Assert.AreEqual(SnapCheck.RunOver, PveRules.AddSnap(s, Run + PveRules.FinalGraceSeconds + 1, 5000, 2, 1800, Run, Bucket));
        }

        [Test]
        public void AddSnap_KeepsOnePerBucket_AndAlwaysTheStart()
        {
            var s = Start();
            PveRules.AddSnap(s, 30, 10, 0, 0, Run, Bucket);      // same bucket as t=0, but t=0 is kept
            PveRules.AddSnap(s, 100, 20, 0, 0, Run, Bucket);     // replaces t=30
            PveRules.AddSnap(s, 599, 30, 0, 0, Run, Bucket);     // replaces t=100
            PveRules.AddSnap(s, 600, 40, 0, 0, Run, Bucket);     // next bucket
            CollectionAssert.AreEqual(new[] { 0, 599, 600 }, s.Select(x => x.t).ToArray());
            Assert.AreEqual(30d, s[1].nw);
        }

        [Test]
        public void AddSnap_ClampsTheFinalSnapshotToTheRunEnd_WithinGrace()
        {
            var s = Start();
            PveRules.AddSnap(s, Run - 100, 500, 1, 1800, Run, Bucket);
            Assert.AreEqual(SnapCheck.Accepted, PveRules.AddSnap(s, Run + 120, 520, 1, 1800, Run, Bucket));
            Assert.AreEqual(Run, s[s.Count - 1].t);
            Assert.AreEqual(SnapCheck.Accepted, PveRules.AddSnap(s, Run + 300, 530, 1, 1800, Run, Bucket));
            Assert.AreEqual(3, s.Count, "a second late snapshot replaces the final one");
            Assert.AreEqual(530d, s[2].nw);
        }

        [Test]
        public void AddSnap_CapsTheReportedIdleCap()
        {
            var s = Start();
            PveRules.AddSnap(s, 700, 10, 1, 999999, Run, Bucket);
            Assert.AreEqual(PveRules.MaxIdleCapSeconds, s[1].cap);
        }

        [Test]
        public void SnapshotTime_DelayedDeliveryCountsAtItsOwnTime_NotInTheFuture()
        {
            Assert.AreEqual(SnapCheck.Accepted, PveRules.SnapshotTime(5000, -1, out int now));
            Assert.AreEqual(5000, now, "no own time: counts at the server's");
            Assert.AreEqual(SnapCheck.Accepted, PveRules.SnapshotTime(90000, 86000, out int late));
            Assert.AreEqual(86000, late, "taken before the 24h mark, delivered later");
            Assert.AreEqual(SnapCheck.Accepted, PveRules.SnapshotTime(5000, 5100, out int skew));
            Assert.AreEqual(5000, skew, "a little device clock drift is clamped to the server's time");
            Assert.AreEqual(SnapCheck.Future, PveRules.SnapshotTime(5000, 5000 + PveRules.BackdateSkewSeconds + 1, out _));
        }

        [Test]
        public void DelayedSnapshot_OlderThanTheLastIsStale()
        {
            var s = Start();
            PveRules.AddSnap(s, 6000, 500, 1, 1800, Run, Bucket);
            PveRules.SnapshotTime(90000, 5000, out int rawT);
            Assert.AreEqual(SnapCheck.Stale, PveRules.AddSnap(s, rawT, 450, 1, 1800, Run, Bucket));
        }

        // ── Value at time t (the docs/pve.txt example) ────────────────────────

        static List<PveSnap> Snaps(params (double hours, double nw, double rate, int cap)[] pts) =>
            pts.Select(p => new PveSnap { t = (int)(p.hours * 3600), nw = p.nw, rate = p.rate, cap = p.cap }).ToList();

        [Test]
        public void ValueAt_PlayerAhead_UsesTheirLastSnapshotBeforeTheViewersTime()
        {
            // Viewer is 10h in; this player is 14h in with snapshots between 10h and 14h.
            var ahead = Snaps((0, 0, 0, 0), (9, 900, 5, 1800), (9.5, 950, 5, 1800), (11, 1100, 5, 1800), (14, 1400, 5, 1800));
            var (v, projected) = PveRules.ValueAt(ahead, 10 * 3600);
            Assert.AreEqual(950d, v);
            Assert.IsFalse(projected);
        }

        [Test]
        public void ValueAt_PlayerBehind_IsProjectedAtTheirIdleRate_CappedAtTheirIdleCap()
        {
            // This player stopped at 6h; the viewer is 10h in. Idle rate 2/s, idle cap 30 min.
            var behind = Snaps((0, 0, 0, 0), (6, 600, 2, 1800));
            var (v, projected) = PveRules.ValueAt(behind, 10 * 3600);
            Assert.AreEqual(600d + 2 * 1800, v, "4h away, but idle earnings stop at the 30 min cap");
            Assert.IsTrue(projected);

            var uncapped = Snaps((0, 0, 0, 0), (6, 600, 2, 43200));
            Assert.AreEqual(600d + 2 * 4 * 3600, PveRules.ValueAt(uncapped, 10 * 3600).value);
        }

        [Test]
        public void ValueAt_ExactSnapshotTime_IsReal()
        {
            var s = Snaps((0, 0, 0, 0), (10, 1000, 3, 1800));
            var (v, projected) = PveRules.ValueAt(s, 10 * 3600);
            Assert.AreEqual(1000d, v);
            Assert.IsFalse(projected);
        }

        // ── Ranking, moves, rewards ───────────────────────────────────────────

        static PveMember M(string id, string start, params (double h, double nw)[] pts) => new PveMember
        {
            playerId = id, name = id, startUtc = start,
            snaps = pts.Select(p => new PveSnap { t = (int)(p.h * 3600), nw = p.nw }).ToList(),
        };

        [Test]
        public void Rank_HighestValueFirst_TiesToTheEarlierStart()
        {
            var ranked = PveRules.Rank(new[]
            {
                M("a", "2026-11-27T05:00:00Z", (0, 0), (24, 500)),
                M("b", "2026-11-27T01:00:00Z", (0, 0), (24, 800)),
                M("c", "2026-11-27T02:00:00Z", (0, 0), (24, 500)),
            }, Run);
            CollectionAssert.AreEqual(new[] { "b", "c", "a" }, ranked.Select(r => r.member.playerId).ToArray());
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, ranked.Select(r => r.rank).ToArray());
        }

        [Test]
        public void Moves_FullBracket_UsesTheTable_TopAndBottomLeaguesHoldTheirEnds()
        {
            var cfg = Ladder(("bronze", 10, 3, 2), ("silver", 10, 3, 2), ("gold", 10, 3, 2));
            Assert.AreEqual((3, 0), PveRules.Moves(cfg, cfg.leagues[0], 10, 10), "bronze never demotes");
            Assert.AreEqual((3, 2), PveRules.Moves(cfg, cfg.leagues[1], 10, 10));
            Assert.AreEqual((0, 2), PveRules.Moves(cfg, cfg.leagues[2], 10, 10), "the top league never promotes");
        }

        [Test]
        public void Moves_SmallBracket_ScalesDown_AtLeastOneFromThree()
        {
            var cfg = Ladder(("bronze", 100, 15, 0), ("silver", 75, 12, 10), ("gold", 50, 8, 8));
            Assert.AreEqual((7, 0), PveRules.Moves(cfg, cfg.leagues[0], 50, 100));
            Assert.AreEqual((1, 1), PveRules.Moves(cfg, cfg.leagues[1], 3, 75), "tiny bracket still moves one each way");
            Assert.AreEqual((0, 0), PveRules.Moves(cfg, cfg.leagues[1], 2, 75), "two players: floor scaling, no minimum");
            Assert.AreEqual((0, 0), PveRules.Moves(cfg, cfg.leagues[1], 1, 75));
            var (up, down) = PveRules.Moves(Ladder(("a", 2, 0, 0), ("b", 4, 3, 3), ("c", 2, 0, 0)), Ladder(("a", 2, 0, 0), ("b", 4, 3, 3), ("c", 2, 0, 0)).leagues[1], 4, 4);
            Assert.LessOrEqual(up + down, 4);
        }

        [Test]
        public void NextLeague_PromotesTopAndDemotesBottom()
        {
            var cfg = Ladder(("bronze", 10, 3, 0), ("silver", 10, 3, 2), ("gold", 10, 0, 2));
            Assert.AreEqual(("gold", "promoted"),  PveRules.NextLeague(cfg, "silver", 3, 10, 3, 2));
            Assert.AreEqual(("silver", "stayed"),  PveRules.NextLeague(cfg, "silver", 4, 10, 3, 2));
            Assert.AreEqual(("silver", "stayed"),  PveRules.NextLeague(cfg, "silver", 8, 10, 3, 2));
            Assert.AreEqual(("bronze", "demoted"), PveRules.NextLeague(cfg, "silver", 9, 10, 3, 2));
            Assert.AreEqual(("gold", "stayed"),    PveRules.NextLeague(cfg, "gold", 1, 10, 0, 2));
        }

        [Test]
        public void RealConfig_LadderRunsBronzeToChampion_WithRewardsForEveryone()
        {
            var cfg = RealConfig();
            CollectionAssert.AreEqual(new[] { "bronze", "silver", "gold", "platinum", "diamond", "champion" },
                                      cfg.leagues.Select(l => l.id).ToArray());
            Assert.AreEqual(PveRules.StartLeague, cfg.leagues[0].id);
            Assert.AreEqual(24, cfg.run_hours);
            for (int i = 1; i < cfg.leagues.Length; i++)
                Assert.LessOrEqual(cfg.leagues[i].bracket_size, cfg.leagues[i - 1].bracket_size, "brackets shrink as you climb");
            foreach (var l in cfg.leagues)
            {
                var last = l.rewards[l.rewards.Length - 1];
                Assert.AreEqual(0, last.rank_to, $"{l.id}: last band must be the catch-all");
                Assert.AreEqual(0, last.top_percent, $"{l.id}: last band must be the catch-all");
                for (int rank = 1; rank <= l.bracket_size; rank++)
                    Assert.IsNotNull(PveRules.RewardFor(l, rank, l.bracket_size), $"{l.id} rank {rank}");
            }
        }

        [Test]
        public void RewardFor_BandsInOrder()
        {
            var bronze = RealConfig().leagues[0];
            Assert.AreEqual(150, PveRules.RewardFor(bronze, 1, 100).crystals);
            Assert.AreEqual(100, PveRules.RewardFor(bronze, 3, 100).crystals);
            Assert.AreEqual(60,  PveRules.RewardFor(bronze, 10, 100).crystals);
            Assert.AreEqual(30,  PveRules.RewardFor(bronze, 25, 100).crystals, "top 25%");
            Assert.AreEqual(10,  PveRules.RewardFor(bronze, 26, 100).crystals);
        }

        [Test]
        public void Ids_AreStableAndSeparateDevBrackets()
        {
            Assert.AreEqual("pve_2026-11-27_bronze_3", PveRules.BracketId("2026-11-27", "bronze", false, 3));
            Assert.AreEqual("pve_2026-11-27_bronze_dev_1", PveRules.BracketId("2026-11-27", "bronze", true, 1));
            Assert.AreEqual("m_abc", PveRules.MemberKey("abc"));
        }

        // ── JSON compatibility with the server ────────────────────────────────

        [Test]
        public void ServerStyleJson_WithNullsAndNesting_ParsesWithJsonUtility()
        {
            // The module writes System.Text.Json with fields, nulls included.
            const string json = "{\"error\":null,\"serverNowUtc\":\"2026-11-27T10:00:00.0000000Z\",\"week\":\"2026-11-27\"," +
                "\"phase\":\"Entry\",\"phaseEndsUtc\":\"2026-11-28T00:00:00.0000000Z\",\"nextEntryUtc\":null," +
                "\"profile\":{\"league\":\"silver\",\"lastWeek\":\"2026-11-27\",\"bracketId\":\"pve_2026-11-27_silver_1\"," +
                "\"history\":[{\"week\":\"2026-11-20\",\"league\":\"bronze\",\"rank\":2,\"size\":5,\"score\":12345.5,\"result\":\"promoted\",\"newLeague\":\"silver\"}]," +
                "\"pendingRewards\":[{\"week\":\"2026-11-20\",\"league\":\"bronze\",\"rank\":2,\"size\":5,\"crystals\":100,\"stars\":3}]}," +
                "\"joinedThisWeek\":true,\"startUtc\":null,\"runEndsUtc\":null,\"bracketId\":\"pve_2026-11-27_silver_1\",\"bracketSettled\":false}";
            var r = JsonUtility.FromJson<PveStatusResponse>(json);
            Assert.IsTrue(string.IsNullOrEmpty(r.error));
            Assert.AreEqual("silver", r.profile.league);
            Assert.AreEqual("promoted", r.profile.history[0].result);
            Assert.AreEqual(3, r.profile.pendingRewards[0].stars);
            Assert.IsTrue(r.joinedThisWeek);
        }

        [Test]
        public void Profile_DefaultsToBronze()
        {
            Assert.AreEqual("bronze", new PveProfile().league);
            Assert.AreEqual("bronze", JsonUtility.FromJson<PveProfile>("{}").league);
        }

        // ── Client wrapper ────────────────────────────────────────────────────

        sealed class FakeTransport : IPveTransport
        {
            public string LastFunction;
            public Dictionary<string, object> LastArgs;
            public string Reply = "{}";
            public Exception Throw;
            public Task<string> Call(string function, Dictionary<string, object> args)
            {
                LastFunction = function; LastArgs = args;
                return Throw != null ? Task.FromException<string>(Throw) : Task.FromResult(Reply);
            }
        }

        IPveTransport _prevTransport;
        DateTime? _prevDebug;

        [SetUp] public void SetUp() { _prevTransport = PveApi.Transport; _prevDebug = PveApi.DebugNowUtc; }
        [TearDown] public void TearDown() { PveApi.Transport = _prevTransport; PveApi.DebugNowUtc = _prevDebug; }

        [Test]
        public void Api_SendsNamedArgs_AndDebugTimeOnlyWhenSet()
        {
            var fake = new FakeTransport { Reply = "{\"t\":120,\"accepted\":true}" };
            PveApi.Transport = fake;
            PveApi.DebugNowUtc = null;

            var r = PveApi.Snapshot(1000, 2.5, 1800).GetAwaiter().GetResult();
            Assert.AreEqual("Snapshot", fake.LastFunction);
            Assert.AreEqual(1000d, fake.LastArgs["netWorth"]);
            Assert.AreEqual(2.5d, fake.LastArgs["rate"]);
            Assert.AreEqual(1800, fake.LastArgs["cap"]);
            Assert.IsFalse(fake.LastArgs.ContainsKey("debugNow"));
            Assert.IsTrue(r.accepted);
            Assert.AreEqual(120, r.t);

            PveApi.DebugNowUtc = Fri.AddHours(3);
            PveApi.Status().GetAwaiter().GetResult();
            Assert.AreEqual("2026-11-27T03:00:00.0000000Z", fake.LastArgs["debugNow"]);
        }

        [Test]
        public void Api_TransportFailure_BecomesANetworkError()
        {
            PveApi.Transport = new FakeTransport { Throw = new InvalidOperationException("offline") };
            var r = PveApi.Board().GetAwaiter().GetResult();
            StringAssert.StartsWith("network:", r.error);

            PveApi.Transport = new FakeTransport { Reply = "" };
            StringAssert.StartsWith("network:", PveApi.Status().GetAwaiter().GetResult().error);
        }
    }
}
