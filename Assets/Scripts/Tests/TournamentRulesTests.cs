using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// PvE phase 4: the client tournament rules (unlock, run timer, prestige lock, pending join prestige,
    /// snapshot timing, panel state and text), the save field, the HUD markup, and TournamentService's join
    /// and server mirroring against a fake transport.
    /// </summary>
    public class TournamentRulesTests
    {
        static readonly DateTime Start = new DateTime(2026, 11, 27, 10, 0, 0, DateTimeKind.Utc);   // a Friday

        static TournamentSaveData Entry(int prestigeAtJoin = -1, bool finalSent = false) => new TournamentSaveData
        {
            week = "2026-11-27", bracketId = "pve_2026-11-27_bronze_1", league = "bronze",
            startUtc = PveRules.Iso(Start), runEndsUtc = PveRules.Iso(Start.AddHours(24)),
            prestigeCountAtJoin = prestigeAtJoin, finalSent = finalSent,
        };

        // ── Unlock, run, lock ─────────────────────────────────────────────────

        [Test]
        public void Unlock_NeedsTheChemistryTutorialDone()
        {
            var save = new SaveData();
            Assert.IsFalse(TournamentRules.IsUnlocked(save));
            TutorialFlows.Slot(save, TournamentRules.UnlockWorld, create: true).done = false;
            Assert.IsFalse(TournamentRules.IsUnlocked(save));
            TutorialFlows.Slot(save, TournamentRules.UnlockWorld, create: false).done = true;
            Assert.IsTrue(TournamentRules.IsUnlocked(save));
            Assert.IsFalse(TournamentRules.IsUnlocked(null));
        }

        [Test]
        public void RunWindow_LocksPrestigeForTwentyFourHours()
        {
            var t = Entry();
            Assert.IsTrue(TournamentRules.InRun(t, Start.AddHours(1)));
            Assert.IsTrue(TournamentRules.PrestigeLocked(t, Start.AddHours(23.9)));
            Assert.IsFalse(TournamentRules.PrestigeLocked(t, Start.AddHours(24)));
            Assert.AreEqual(TimeSpan.FromHours(14), TournamentRules.Remaining(t, Start.AddHours(10)));
            Assert.AreEqual(TimeSpan.Zero, TournamentRules.Remaining(t, Start.AddHours(30)));
            Assert.IsFalse(TournamentRules.InRun(new TournamentSaveData(), Start), "never entered");
            Assert.IsFalse(TournamentRules.InRun(null, Start));
        }

        [Test]
        public void JoinPrestige_IsPendingUntilThePrestigeCountPassesTheJoin()
        {
            var t = Entry(prestigeAtJoin: 4);
            Assert.IsTrue(TournamentRules.PrestigePending(t, 4));
            Assert.IsFalse(TournamentRules.PrestigePending(t, 5));
            Assert.IsFalse(TournamentRules.PrestigePending(Entry(prestigeAtJoin: -1), 0), "-1 = restored from the server, never pending");
        }

        // ── Snapshot timing ───────────────────────────────────────────────────

        [Test]
        public void Snapshots_NeverBeforeTheJoinPrestige()
        {
            var t = Entry(prestigeAtJoin: 2);
            Assert.IsFalse(TournamentRules.ShouldSnapshot(t, Start.AddMinutes(1), prestigeCount: 2, secondsSinceLast: 999, force: true),
                           "the old run's net worth would make every later snapshot look like a drop");
            Assert.IsTrue(TournamentRules.ShouldSnapshot(t, Start.AddMinutes(1), prestigeCount: 3, secondsSinceLast: 0, force: true));
        }

        [Test]
        public void Snapshots_EveryFiveMinutesOfPlay_OrWhenForced()
        {
            var t = Entry();
            Assert.IsFalse(TournamentRules.ShouldSnapshot(t, Start.AddHours(2), 1, 299f, false));
            Assert.IsTrue(TournamentRules.ShouldSnapshot(t, Start.AddHours(2), 1, 300f, false));
            Assert.IsTrue(TournamentRules.ShouldSnapshot(t, Start.AddHours(2), 1, 5f, true));
        }

        [Test]
        public void Snapshots_OneFinalAfterTheMark_WithinTheGrace()
        {
            var end = Start.AddHours(24);
            Assert.IsTrue(TournamentRules.ShouldSnapshot(Entry(), end.AddSeconds(30), 1, 0f, false));
            Assert.IsFalse(TournamentRules.ShouldSnapshot(Entry(finalSent: true), end.AddSeconds(30), 1, 999f, true));
            Assert.IsFalse(TournamentRules.ShouldSnapshot(Entry(), end.AddSeconds(PveRules.FinalGraceSeconds + 1), 1, 999f, true),
                           "past the grace the server would refuse it");
            Assert.IsFalse(TournamentRules.ShouldSnapshot(new TournamentSaveData(), Start, 1, 999f, true));
        }

        // ── Panel state ───────────────────────────────────────────────────────

        static PveStatusResponse Status(string phase, bool joined = false, bool settled = false) => new PveStatusResponse
        {
            week = "2026-11-27", phase = phase, joinedThisWeek = joined, bracketSettled = settled,
            phaseEndsUtc = PveRules.Iso(Start.Date.AddDays(1)), profile = new PveProfile(),
        };

        static TournamentPanelState State(PveStatusResponse s, TournamentSaveData t, DateTime now,
                                          bool enabled = true, bool unlocked = true) =>
            TournamentRules.PanelState(enabled, unlocked, s, t, now);

        [Test]
        public void PanelState_CoversTheWeek()
        {
            var fri = Start; var sat = Start.AddDays(1).AddHours(2); var sun = Start.AddDays(2).AddHours(2);
            Assert.AreEqual(TournamentPanelState.Disabled, State(Status("Entry"), null, fri, enabled: false));
            Assert.AreEqual(TournamentPanelState.Locked,   State(Status("Entry"), null, fri, unlocked: false));
            Assert.AreEqual(TournamentPanelState.Offline,  State(null, null, fri));
            Assert.AreEqual(TournamentPanelState.Offline,  State(new PveStatusResponse { error = "network:x" }, null, fri));
            Assert.AreEqual(TournamentPanelState.Open,     State(Status("Entry"), null, fri));
            Assert.AreEqual(TournamentPanelState.Running,  State(Status("Entry", joined: true), Entry(), fri.AddHours(1)));
            Assert.AreEqual(TournamentPanelState.Running,  State(null, Entry(), fri.AddHours(1)), "the run timer works offline");
            Assert.AreEqual(TournamentPanelState.Finished, State(Status("Running", joined: true), Entry(), sat.AddHours(10)));
            Assert.AreEqual(TournamentPanelState.Results,  State(Status("Results", joined: true, settled: true), Entry(), sun));
            Assert.AreEqual(TournamentPanelState.Waiting,  State(Status("Results"), null, sun), "didn't enter this week");
        }

        // ── Panel text ────────────────────────────────────────────────────────

        static PveConfig Cfg() => new PveConfig
        {
            leagues = new[]
            {
                new PveLeagueDef { id = "bronze", name = "Bronze" },
                new PveLeagueDef { id = "silver", name = "Silver" },
            },
        };

        [Test]
        public void Text_OpenShowsStart_RunningShowsTimeLeft()
        {
            var open = TournamentPanelText.Describe(TournamentPanelState.Open, Status("Entry"), null, Start, Cfg(), false);
            Assert.IsTrue(open.showStart);
            StringAssert.Contains("Bronze league", open.headline);
            StringAssert.Contains("Entry closes in 14h 00m", open.timer);

            var running = TournamentPanelText.Describe(TournamentPanelState.Running, Status("Entry", true), Entry(), Start.AddHours(10), Cfg(), false);
            Assert.IsFalse(running.showStart);
            Assert.AreEqual("14h 00m left", running.timer);

            var starting = TournamentPanelText.Describe(TournamentPanelState.Running, null, Entry(2), Start, Cfg(), prestigePending: true);
            StringAssert.Contains("Starting", starting.headline);
        }

        [Test]
        public void Text_ResultsShowRankMoveAndWaitingReward()
        {
            var s = Status("Results", joined: true, settled: true);
            s.profile.history.Add(new PveHistoryEntry { week = "2026-11-27", league = "bronze", rank = 2, size = 5, result = "promoted", newLeague = "silver" });
            s.profile.pendingRewards.Add(new PveReward { week = "2026-11-27", crystals = 100, stars = 3 });
            var d = TournamentPanelText.Describe(TournamentPanelState.Results, s, Entry(), Start.AddDays(2).AddHours(1), Cfg(), false);
            Assert.AreEqual("Last tournament: rank 2 of 5, promoted to Silver. Reward waiting: 100 ◆ + 3 ★", d.result);
            Assert.AreEqual("", TournamentPanelText.LastResult(Status("Entry"), Cfg()), "no history yet");
        }

        [Test]
        public void Text_Helpers()
        {
            Assert.AreEqual(1,  TournamentPanelText.Zone(2, 10, 3, 2));
            Assert.AreEqual(0,  TournamentPanelText.Zone(5, 10, 3, 2));
            Assert.AreEqual(-1, TournamentPanelText.Zone(9, 10, 3, 2));
            Assert.AreEqual(0,  TournamentPanelText.Zone(10, 10, 3, 0), "no demotion zone in the bottom league");
            StringAssert.StartsWith("~", TournamentPanelText.ValueText(new PveBoardRow { value = 1200, projected = true }));
            Assert.AreEqual("6.5h", TournamentPanelText.PlayedText(6 * 3600 + 1800));
            Assert.AreEqual("1d 2h", TournamentRules.FormatSpan(TimeSpan.FromHours(26)));
            Assert.AreEqual("5m 07s", TournamentRules.FormatSpan(TimeSpan.FromSeconds(307)));
            StringAssert.Contains("+1,234 ✦", TournamentRules.JoinConfirmMessage(1234, "Gold"));
            StringAssert.Contains("Gold bracket", TournamentRules.JoinConfirmMessage(0, "Gold"));
            StringAssert.Contains("🔒", TournamentRules.PrestigeLockText(TimeSpan.FromHours(3)));
            StringAssert.Contains("connection", TournamentRules.ErrorText("network:timeout"));
            StringAssert.Contains("player name", TournamentRules.ErrorText(PveErrors.NameRequired));
        }

        // ── Save + HUD ────────────────────────────────────────────────────────

        [Test]
        public void Save_LegacyPvpRunStillLoads_TournamentDefaultsToNotPending()
        {
            var save = JsonUtility.FromJson<SaveData>("{\"pvpRun\":{\"state\":\"InRun\",\"runStartUtc\":\"2026-01-01T00:00:00Z\"}}");
            Assert.AreEqual("InRun", save.pvpRun.state);
            Assert.IsNotNull(save.tournament);
            Assert.AreEqual(-1, save.tournament.prestigeCountAtJoin);
            Assert.IsFalse(TournamentRules.InRun(save.tournament, Start));
        }

        [Test]
        public void Save_TournamentRoundTrips()
        {
            var save = new SaveData { tournament = Entry(prestigeAtJoin: 3, finalSent: true) };
            var back = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(save));
            Assert.AreEqual("pve_2026-11-27_bronze_1", back.tournament.bracketId);
            Assert.AreEqual(3, back.tournament.prestigeCountAtJoin);
            Assert.IsTrue(back.tournament.finalSent);
        }

        [Test]
        public void Prestige_KeepsTheTournamentEntry()
        {
            var save = new SaveData { tournament = Entry(prestigeAtJoin: 0) };
            PrestigeRunReset.ResetRun(save, 250);
            Assert.AreEqual("pve_2026-11-27_bronze_1", save.tournament.bracketId);
        }

        [Test]
        public void Hud_HasTheTournamentPanelElements()
        {
            var root = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/GameHUD.uxml").Instantiate();
            Assert.AreEqual("Tournament", root.Q<Button>("btn-pvp").text);
            foreach (var n in new[] { "pvp-panel", "pvp-state-label", "pvp-timer-label", "pvp-lock-label", "pvp-completed-label", "pvp-leaderboard-list" })
                Assert.IsNotNull(root.Q(n), n);
            Assert.IsNotNull(root.Q<Button>("btn-enter-pvp"));
            Assert.IsNotNull(root.Q<Button>("btn-refresh-leaderboard"));
        }

        [Test]
        public void FeatureFlag_PveIsOnByDefault() => Assert.IsTrue(FeatureFlags.PveEnabledDefault);

        // ── TournamentService against a fake server ───────────────────────────

        sealed class FakeTransport : IPveTransport
        {
            public readonly Dictionary<string, string> Replies = new();
            public readonly List<string> Calls = new();
            public Task<string> Call(string function, Dictionary<string, object> args)
            {
                Calls.Add(function);
                return Task.FromResult(Replies.TryGetValue(function, out var r) ? r : "{}");
            }
        }

        IPveTransport _prevTransport;
        GameObject _saveManagerGO, _serviceGO;
        string _savePath, _saveBackup;

        [SetUp]
        public void SetUp()
        {
            _prevTransport = PveApi.Transport;
            _savePath = Path.Combine(LocalSaveService.SaveDirectory, "save.json");
            _saveBackup = File.Exists(_savePath) ? File.ReadAllText(_savePath) : null;
        }

        [TearDown]
        public void TearDown()
        {
            PveApi.Transport = _prevTransport;
            if (_serviceGO != null) UnityEngine.Object.DestroyImmediate(_serviceGO);
            if (_saveManagerGO != null)
            {
                UnityEngine.Object.DestroyImmediate(_saveManagerGO);
                new LocalSaveService().Delete();
                if (_saveBackup != null) File.WriteAllText(_savePath, _saveBackup);
            }
        }

        TournamentService Spawn(int prestigeCount)
        {
            new LocalSaveService().Delete();
            _saveManagerGO = new GameObject("SaveManager");
            var sm = _saveManagerGO.AddComponent<SaveManager>();
            RunAwake(sm);
            SaveManager.Instance.Current.prestigeCount = prestigeCount;
            _serviceGO = new GameObject("TournamentService");
            var svc = _serviceGO.AddComponent<TournamentService>();
            RunAwake(svc);
            return svc;
        }

        static void RunAwake(MonoBehaviour mb)
        {
            for (var t = mb.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            {
                var m = t.GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (m != null) { m.Invoke(mb, null); return; }
            }
        }

        static string JoinedJson(string start) =>
            "{\"week\":\"2026-11-27\",\"phase\":\"Entry\",\"joinedThisWeek\":true,\"bracketId\":\"pve_2026-11-27_bronze_dev_1\"," +
            $"\"startUtc\":\"{start}\",\"runEndsUtc\":\"{PveRules.Iso(PveRules.ParseIso(start).AddHours(24))}\"," +
            "\"profile\":{\"league\":\"bronze\",\"lastWeek\":\"2026-11-27\"}}";

        [Test]
        public void Join_RecordsTheEntry_WithThePrestigeStillPending()
        {
            var svc = Spawn(prestigeCount: 6);
            var fake = new FakeTransport();
            fake.Replies["Join"] = JoinedJson(PveRules.Iso(Start));
            PveApi.Transport = fake;

            var r = svc.JoinAsync().GetAwaiter().GetResult();
            Assert.IsTrue(string.IsNullOrEmpty(r.error));
            var t = SaveManager.Instance.Current.tournament;
            Assert.AreEqual("pve_2026-11-27_bronze_dev_1", t.bracketId);
            Assert.AreEqual(6, t.prestigeCountAtJoin);
            Assert.IsTrue(TournamentRules.PrestigePending(t, SaveManager.Instance.Current.prestigeCount));
        }

        [Test]
        public void Join_ServerRefusal_LeavesTheSaveAlone()
        {
            var svc = Spawn(prestigeCount: 1);
            var fake = new FakeTransport();
            fake.Replies["Join"] = "{\"error\":\"outside_window\"}";
            PveApi.Transport = fake;

            var r = svc.JoinAsync().GetAwaiter().GetResult();
            Assert.AreEqual(PveErrors.OutsideWindow, r.error);
            Assert.IsNull(SaveManager.Instance.Current.tournament.bracketId);
        }

        [Test]
        public void Refresh_RestoresARunEnteredElsewhere_WithoutAPendingPrestige()
        {
            var svc = Spawn(prestigeCount: 3);
            var fake = new FakeTransport();
            fake.Replies["Status"] = JoinedJson(PveRules.Iso(Start));
            PveApi.Transport = fake;

            svc.RefreshAsync().GetAwaiter().GetResult();
            var t = SaveManager.Instance.Current.tournament;
            Assert.AreEqual("pve_2026-11-27_bronze_dev_1", t.bracketId);
            Assert.AreEqual(-1, t.prestigeCountAtJoin, "this device never prestiged for it, so it must not now");
            Assert.IsNotNull(svc.LastStatus);
        }
    }
}
