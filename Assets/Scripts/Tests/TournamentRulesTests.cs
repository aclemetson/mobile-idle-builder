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
            Assert.IsTrue(TournamentRules.PrestigeLeavesRun(t, Start.AddHours(23.9)));
            Assert.IsFalse(TournamentRules.PrestigeLeavesRun(t, Start.AddHours(24)));
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

        // ── Kept snapshot (delivered later if the app was suspended) ─────────

        [Test]
        public void KeptSnapshot_OnlyDuringTheRun_AfterTheJoinPrestige_NeverOlder()
        {
            var t = Entry(prestigeAtJoin: 2);
            Assert.IsFalse(TournamentRules.CapturePending(t, Start.AddHours(1), 2, 100, 1, 1800), "prestige still pending");
            Assert.IsTrue(TournamentRules.CapturePending(t, Start.AddHours(1), 3, 100, 1, 1800));
            Assert.AreEqual(3600, t.pendingT);
            Assert.IsFalse(TournamentRules.CapturePending(t, Start.AddMinutes(30), 3, 50, 1, 1800), "older than the kept one");
            Assert.IsTrue(TournamentRules.CapturePending(t, Start.AddHours(23), 3, 900, 2, 1800));
            Assert.AreEqual(900d, t.pendingNw);
            Assert.IsFalse(TournamentRules.CapturePending(t, Start.AddHours(25), 3, 99999, 2, 1800),
                           "never keeps a value from after the 24h mark");
            Assert.AreEqual(23 * 3600, t.pendingT);
        }

        [Test]
        public void KeptSnapshot_ClearedByTheSendThatCarriedIt_NotByAnOlderOne()
        {
            var t = Entry(); t.pendingT = 5000;
            TournamentRules.ClearPendingIfCovered(t, 4000);
            Assert.AreEqual(5000, t.pendingT, "a newer one was kept while that send was in flight");
            TournamentRules.ClearPendingIfCovered(t, 5000);
            Assert.AreEqual(-1, t.pendingT);
        }

        [Test]
        public void KeptSnapshot_IsDeliveredOnceTheRunIsOver()
        {
            var t = Entry(prestigeAtJoin: 1); t.pendingT = 80000;
            Assert.IsFalse(TournamentRules.ShouldSendPending(t, Start.AddHours(23), 2), "during the run the next live one supersedes it");
            Assert.IsTrue(TournamentRules.ShouldSendPending(t, Start.AddHours(30), 2));
            Assert.IsFalse(TournamentRules.ShouldSendPending(Entry(), Start.AddHours(30), 2), "nothing kept");
        }

        // ── Retire / leave early (#505) ───────────────────────────────────────

        [Test]
        public void Retired_EndsTheRunForThisDevice()
        {
            var t = Entry(prestigeAtJoin: 1);
            Assert.IsTrue(TournamentRules.InRun(t, Start.AddHours(2)));
            t.retired = true;
            Assert.IsFalse(TournamentRules.InRun(t, Start.AddHours(2)));
            Assert.IsFalse(TournamentRules.PrestigeLeavesRun(t, Start.AddHours(2)), "prestige is free again");
            Assert.IsFalse(TournamentRules.ShouldSnapshot(t, Start.AddHours(2), 2, 999f, force: true), "no more snapshots");
            Assert.IsFalse(TournamentRules.CapturePending(t, Start.AddHours(2), 2, 1, 1, 1));
            Assert.AreEqual(TournamentPanelState.Finished,
                TournamentRules.PanelState(true, true, Status("Entry", joined: true), t, Start.AddHours(2)));
        }

        [Test]
        public void Retire_Texts()
        {
            StringAssert.Contains("ends it early", TournamentRules.PrestigeLeaveMessage(1500, TimeSpan.FromHours(5)));
            StringAssert.Contains("5h 00m left", TournamentRules.RetireMessage(1500, TimeSpan.FromHours(5)));
            var running = TournamentPanelText.Describe(TournamentPanelState.Running, Status("Entry", true), Entry(), Start.AddHours(1), Cfg(), false);
            Assert.IsTrue(running.showRetire);
            var pending = TournamentPanelText.Describe(TournamentPanelState.Running, null, Entry(2), Start, Cfg(), prestigePending: true);
            Assert.IsFalse(pending.showRetire, "not before the join's prestige has run");
            var retired = Entry(); retired.retired = true;
            var done = TournamentPanelText.Describe(TournamentPanelState.Finished, Status("Entry", true), retired, Start.AddHours(1), Cfg(), false);
            Assert.AreEqual("You retired from this tournament", done.headline);
            StringAssert.EndsWith("(retired)", TournamentPanelText.ValueText(new PveBoardRow { value = 900, retired = true, projected = true }));
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
        public void Text_Disabled_SaysComingSoon_WithNoStart()
        {
            // pve.enabled off (production until PvE launches): the button stays and the panel says Coming Soon.
            var d = TournamentPanelText.Describe(TournamentPanelState.Disabled, null, null, Start, Cfg(), false);
            Assert.AreEqual("Coming Soon", d.headline);
            Assert.IsFalse(string.IsNullOrEmpty(d.info));
            Assert.IsFalse(d.showStart);
            Assert.IsFalse(d.showRetire);
        }

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
            Assert.AreEqual("⬆ Prestige · 🏆 3h 00m", TournamentRules.PrestigeInRunText(TimeSpan.FromHours(3)));
            StringAssert.Contains("connection", TournamentRules.ErrorText("network:timeout"));
            StringAssert.Contains("player name", TournamentRules.ErrorText(PveErrors.NameRequired));
        }

        // ── Rewards (phase 5) ─────────────────────────────────────────────────

        static PveReward Reward(string week = "2026-11-27", int crystals = 150, int stars = 5) =>
            new PveReward { week = week, league = "bronze", rank = 1, size = 5, crystals = crystals, stars = stars };

        [Test]
        public void ApplyReward_GivesCrystalsAndStars_Once()
        {
            var save = new SaveData { paidCurrency = 10 };
            var pool = new[] { "carbon", "oxygen" };
            var landed = TournamentRules.ApplyReward(save, Reward(), pool, new System.Random(3));
            Assert.AreEqual(160, save.paidCurrency);
            Assert.AreEqual(5, landed.Count);
            Assert.AreEqual(5, ItemStars.Total(save));
            Assert.IsTrue(TournamentRules.RewardApplied(save, "2026-11-27"));

            var again = TournamentRules.ApplyReward(save, Reward(), pool, new System.Random(3));
            Assert.AreEqual(0, again.Count, "a retried claim never pays twice");
            Assert.AreEqual(160, save.paidCurrency);
            Assert.AreEqual(5, ItemStars.Total(save));
        }

        [Test]
        public void ApplyReward_KeepsOnlyTheRecentWeeks()
        {
            var save = new SaveData();
            for (int i = 0; i < TournamentRules.ClaimedWeeksKept + 5; i++)
                TournamentRules.ApplyReward(save, Reward(week: $"w{i:00}", stars: 0), new string[0], new System.Random(1));
            Assert.AreEqual(TournamentRules.ClaimedWeeksKept, save.pveClaimedWeeks.Count);
            Assert.AreEqual($"w{TournamentRules.ClaimedWeeksKept + 4:00}", save.pveClaimedWeeks[save.pveClaimedWeeks.Count - 1]);
        }

        [Test]
        public void NextUnappliedReward_SkipsWeeksThisSaveAlreadyHas()
        {
            var save = new SaveData();
            save.pveClaimedWeeks.Add("2026-11-20");
            var status = new PveStatusResponse { profile = new PveProfile() };
            status.profile.pendingRewards.Add(Reward(week: "2026-11-20"));
            status.profile.pendingRewards.Add(Reward(week: "2026-11-27"));
            Assert.AreEqual("2026-11-27", TournamentRules.NextUnappliedReward(status, save).week);
            save.pveClaimedWeeks.Add("2026-11-27");
            Assert.IsNull(TournamentRules.NextUnappliedReward(status, save));
        }

        [Test]
        public void Text_BannerClaimRankAndReveal()
        {
            var s = Status("Results", joined: true, settled: true);
            s.profile.history.Add(new PveHistoryEntry { week = "2026-11-27", rank = 1, size = 5, result = "promoted", newLeague = "silver" });
            Assert.AreEqual(("Promoted to Silver!", "promoted"), TournamentPanelText.Banner(s, Cfg(), TournamentPanelState.Results));
            Assert.AreEqual(("", ""), TournamentPanelText.Banner(s, Cfg(), TournamentPanelState.Running), "no banner mid-run");
            s.profile.history[0].result = "demoted"; s.profile.history[0].newLeague = "bronze";
            Assert.AreEqual(("Moved down to Bronze", "demoted"), TournamentPanelText.Banner(s, Cfg(), TournamentPanelState.Waiting));

            Assert.AreEqual("Claim 150 ◆ + 5 ★", TournamentPanelText.ClaimButtonText(Reward()));
            Assert.AreEqual("Claim 3 ★", TournamentPanelText.ClaimButtonText(Reward(crystals: 0, stars: 3)));

            var board = new PveBoardResponse();
            board.rows.Add(new PveBoardRow { rank = 1, name = "a" });
            board.rows.Add(new PveBoardRow { rank = 2, name = "me", you = true });
            Assert.AreEqual("You: #2 of 2", TournamentPanelText.YourRank(board, TournamentPanelState.Running));
            Assert.AreEqual("", TournamentPanelText.YourRank(board, TournamentPanelState.Open));

            string reveal = TournamentPanelText.RevealText(Reward(), new[] { "carbon", "oxygen", "carbon" },
                id => id == "carbon" ? "Carbon" : "Oxygen", id => id == "carbon" ? 5 : 1, Cfg());
            StringAssert.StartsWith("Rank 1 of 5 in Bronze.", reveal);
            StringAssert.Contains("+150 ◆", reveal);
            StringAssert.Contains("★×2 Carbon: now ★5 (+100% value)", reveal);
            StringAssert.Contains("★ Oxygen: now ★1 (+20% value)", reveal);
        }

        // ── Onboarding (phase 6) ──────────────────────────────────────────────

        static SaveData Unlocked()
        {
            var save = new SaveData();
            save.tutorial.isActive = false;
            TutorialFlows.Slot(save, TournamentRules.UnlockWorld, create: true).done = true;
            return save;
        }

        [Test]
        public void Intro_OncePerEntryWindow_OnlyWhenUnlockedCalmAndNotEntered()
        {
            var save = Unlocked();
            var fri = Start;                                  // entry open
            Assert.IsTrue(TournamentIntroController.ShouldShow(true, save, fri, calm: true));
            Assert.IsFalse(TournamentIntroController.ShouldShow(false, save, fri, true), "pve.enabled off");
            Assert.IsFalse(TournamentIntroController.ShouldShow(true, save, fri, calm: false), "waits for a calm moment");
            Assert.IsFalse(TournamentIntroController.ShouldShow(true, new SaveData(), fri, true), "locked before the Chemistry tutorial");
            Assert.IsFalse(TournamentIntroController.ShouldShow(true, save, fri.AddDays(1), true), "entry closed (Saturday)");

            save.tournamentIntroWeek = "2026-11-27";
            Assert.IsFalse(TournamentIntroController.ShouldShow(true, save, fri.AddHours(3), true), "already shown this window");
            Assert.IsTrue(TournamentIntroController.ShouldShow(true, save, fri.AddDays(7), true), "next Friday: shown again");

            var entered = Unlocked();
            entered.tournament = Entry();
            Assert.IsFalse(TournamentIntroController.ShouldShow(true, entered, fri.AddHours(1), true), "already entered this week");
        }

        [Test]
        public void Intro_FullExplainerFirst_ThenTheShortOne()
        {
            var save = Unlocked();
            Assert.AreEqual(TournamentIntroController.FullIntroId, TournamentIntroController.DialogueFor(save));
            save.tournamentIntroSeen = true;
            Assert.AreEqual(TournamentIntroController.WeeklyIntroId, TournamentIntroController.DialogueFor(save));
        }

        [Test]
        public void Intro_WaitsForAnyTutorial_IncludingAWorldFlowInProgress()
        {
            var save = Unlocked();
            Assert.IsFalse(TournamentIntroController.AnyTutorialRunning(save, null));
            Assert.IsFalse(TournamentIntroController.AnyTutorialRunning(save, TournamentRules.UnlockWorld), "Chemistry's is done");
            TutorialFlows.Slot(save, "world_biology", create: true).done = false;
            Assert.IsTrue(TournamentIntroController.AnyTutorialRunning(save, "world_biology"));
            save.tutorial.isActive = true;
            Assert.IsTrue(TournamentIntroController.AnyTutorialRunning(save, null));
        }

        static readonly System.Globalization.CultureInfo EnUs = new System.Globalization.CultureInfo("en-US");
        static TimeZoneInfo Zone(double hours) =>
            TimeZoneInfo.CreateCustomTimeZone($"test{hours}", TimeSpan.FromHours(hours), $"test{hours}", $"test{hours}");

        [Test]
        public void LocalTime_ShowsTheWeekdayAndTimeInThePlayersZone()
        {
            // Entry closes Saturday 00:00 UTC: that is Friday evening in the Americas, Saturday morning in Asia.
            string west = TournamentRules.EntryClosesLocal(Start, Zone(-5), EnUs);
            StringAssert.StartsWith("Friday ", west);
            StringAssert.Contains("7:00", west);
            StringAssert.Contains("PM", west);
            string east = TournamentRules.EntryClosesLocal(Start, Zone(9), EnUs);
            StringAssert.StartsWith("Saturday ", east);
            StringAssert.Contains("9:00", east);
            StringAssert.StartsWith("Saturday ", TournamentRules.EntryClosesLocal(Start, Zone(0), EnUs), "UTC itself");

            // Sunday: the next entry opens Friday 00:00 UTC = Thursday 7 PM at UTC-5.
            string next = TournamentRules.NextOpensLocal(Start.AddDays(2), Zone(-5), EnUs);
            StringAssert.StartsWith("Thursday ", next);
            StringAssert.Contains("7:00", next);
        }

        [Test]
        public void Intro_FillsTheEntryCloseTime_AndTheAssetKeepsItsPlaceholder()
        {
            Assert.AreEqual("open until Friday 7:00 PM.",
                TournamentIntroController.FillTimes("open until " + TournamentIntroController.EntryClosesToken + ".", "Friday 7:00 PM"));
            Assert.AreEqual("no token", TournamentIntroController.FillTimes("no token", "x"));

            var weekly = Resources.Load<DialogueDatabaseSO>("DialogueDatabase").Get(TournamentIntroController.WeeklyIntroId);
            StringAssert.Contains(TournamentIntroController.EntryClosesToken, weekly.lines[0].text,
                "filled in a runtime copy only, so next week shows next week's time");
            StringAssert.DoesNotContain("UTC", weekly.lines[0].text);
        }

        [Test]
        public void Intro_DialoguesAreImported()
        {
            var db = Resources.Load<DialogueDatabaseSO>("DialogueDatabase");
            Assert.IsNotNull(db);
            foreach (var id in new[] { TournamentIntroController.FullIntroId, TournamentIntroController.WeeklyIntroId })
            {
                var d = db.Get(id);
                Assert.IsNotNull(d, $"{id} missing: run Import Game Data");
                Assert.Greater(d.lines.Length, 0, id);
            }
        }

        [Test]
        public void Save_IntroFieldsDefaultToNeverShown()
        {
            var save = JsonUtility.FromJson<SaveData>("{\"prestigeCount\":4}");
            Assert.IsFalse(save.tournamentIntroSeen);
            Assert.IsTrue(string.IsNullOrEmpty(save.tournamentIntroWeek));
        }

        // ── Save + HUD ────────────────────────────────────────────────────────

        [Test]
        public void Save_LegacyPvpRunStillLoads_TournamentDefaultsToNotPending()
        {
            var save = JsonUtility.FromJson<SaveData>("{\"pvpRun\":{\"state\":\"InRun\",\"runStartUtc\":\"2026-01-01T00:00:00Z\"}}");
            Assert.AreEqual("InRun", save.pvpRun.state);
            Assert.IsNotNull(save.tournament);
            Assert.AreEqual(-1, save.tournament.prestigeCountAtJoin);
            Assert.AreEqual(-1, save.tournament.pendingT, "nothing kept");
            Assert.IsNotNull(save.pveClaimedWeeks);
            Assert.AreEqual(0, save.pveClaimedWeeks.Count);
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
            Assert.AreEqual("Tourney", DrawerTile.Caption(root.Q<Button>("btn-tournament"))?.text);
            foreach (var n in new[] { "pve-panel", "pve-league-badge", "pve-banner", "pve-state-label", "pve-timer-label", "pve-info-label", "pve-result-label", "pve-your-rank", "pve-leaderboard-list" })
                Assert.IsNotNull(root.Q(n), n);
            Assert.IsNotNull(root.Q<Button>("btn-pve-start"));
            Assert.IsNotNull(root.Q<Button>("btn-pve-claim"));
            Assert.IsNull(root.Q("pvp-panel"), "renamed to pve-panel");
            Assert.IsNotNull(root.Q<Button>("btn-pve-refresh"));
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
        public void SendPending_SendsItsOwnTime_ClearsWhenDoneOrRefused_KeepsOnNetworkError()
        {
            var svc = Spawn(prestigeCount: 3);
            var save = SaveManager.Instance.Current;
            save.tournament = Entry(prestigeAtJoin: 2);
            save.tournament.pendingT = 85000; save.tournament.pendingNw = 4321; save.tournament.pendingRate = 2; save.tournament.pendingCap = 1800;

            var fake = new FakeTransport();
            PveApi.Transport = fake;
            fake.Replies["Snapshot"] = "{\"error\":\"network:offline\"}";
            svc.SendPendingAsync().GetAwaiter().GetResult();
            Assert.AreEqual(85000, save.tournament.pendingT, "kept for the next try");

            fake.Replies["Snapshot"] = "{\"t\":85000,\"accepted\":true}";
            ArgsCapture.Last = null;
            PveApi.Transport = new ArgsCapture(fake);
            svc.SendPendingAsync().GetAwaiter().GetResult();
            Assert.AreEqual(85000, ArgsCapture.Last["atT"]);
            Assert.AreEqual(4321d, ArgsCapture.Last["netWorth"]);
            Assert.AreEqual(-1, save.tournament.pendingT);

            save.tournament.pendingT = 85000;
            fake.Replies["Snapshot"] = "{\"error\":\"run_over\"}";
            PveApi.Transport = fake;
            svc.SendPendingAsync().GetAwaiter().GetResult();
            Assert.AreEqual(-1, save.tournament.pendingT, "settled already: nothing more to deliver");
        }

        sealed class ArgsCapture : IPveTransport
        {
            public static Dictionary<string, object> Last;
            readonly IPveTransport _inner;
            public ArgsCapture(IPveTransport inner) => _inner = inner;
            public Task<string> Call(string function, Dictionary<string, object> args) { Last = args; return _inner.Call(function, args); }
        }

        static string StatusWithReward(string week) =>
            "{\"week\":\"2026-12-04\",\"phase\":\"Entry\",\"profile\":{\"league\":\"silver\",\"pendingRewards\":[" +
            $"{{\"week\":\"{week}\",\"league\":\"bronze\",\"rank\":1,\"size\":5,\"crystals\":150,\"stars\":0}}]}}}}";

        [Test]
        public void Claim_AppliesLocallyFirst_ThenClearsOnTheServer()
        {
            var svc = Spawn(prestigeCount: 3);
            var save = SaveManager.Instance.Current;
            long before = save.paidCurrency;
            var fake = new FakeTransport();
            fake.Replies["Claim"] = "{\"reward\":{\"week\":\"2026-11-27\"}}";
            fake.Replies["Status"] = "{\"week\":\"2026-12-04\",\"phase\":\"Entry\",\"profile\":{\"league\":\"silver\"}}";
            PveApi.Transport = fake;

            var outcome = svc.ClaimAsync(Reward(stars: 0)).GetAwaiter().GetResult();
            Assert.IsTrue(outcome.applied);
            Assert.AreEqual(before + 150, save.paidCurrency);
            Assert.IsTrue(TournamentRules.RewardApplied(save, "2026-11-27"));
            CollectionAssert.Contains(fake.Calls, "Claim");
        }

        [Test]
        public void Claim_ServerStepFails_RewardKept_RetriedLaterWithoutPayingTwice()
        {
            var svc = Spawn(prestigeCount: 3);
            var save = SaveManager.Instance.Current;
            long before = save.paidCurrency;
            var fake = new FakeTransport();
            fake.Replies["Claim"] = "{\"error\":\"network:offline\"}";
            fake.Replies["Status"] = StatusWithReward("2026-11-27");
            PveApi.Transport = fake;

            var outcome = svc.ClaimAsync(Reward(stars: 0)).GetAwaiter().GetResult();
            Assert.IsTrue(outcome.applied);
            StringAssert.StartsWith("network:", outcome.serverError);
            Assert.AreEqual(before + 150, save.paidCurrency);

            // Next refresh: the server still lists it; the device only clears it there.
            fake.Calls.Clear();
            fake.Replies["Claim"] = "{\"reward\":{\"week\":\"2026-11-27\"}}";
            svc.RefreshAsync().GetAwaiter().GetResult();
            CollectionAssert.Contains(fake.Calls, "Claim");
            Assert.AreEqual(before + 150, save.paidCurrency, "never paid twice");
            Assert.IsNull(TournamentRules.NextUnappliedReward(svc.LastStatus, save));
        }

        [Test]
        public void RetireAsync_MarksRetiredOnSuccess_KeepsTheRunOnANetworkError()
        {
            var svc = Spawn(prestigeCount: 3);
            var save = SaveManager.Instance.Current;
            var now = DateTime.UtcNow;
            save.tournament = new TournamentSaveData
            {
                week = PveRules.WeekId(now), startUtc = PveRules.Iso(now.AddHours(-2)), runEndsUtc = PveRules.Iso(now.AddHours(22)),
                prestigeCountAtJoin = 2, pendingT = 7000,
            };
            var fake = new FakeTransport();
            PveApi.Transport = fake;

            fake.Replies["Retire"] = "{\"error\":\"network:offline\"}";
            StringAssert.StartsWith("network:", svc.RetireAsync().GetAwaiter().GetResult());
            Assert.IsFalse(save.tournament.retired, "offline: still in the run (prestige not done)");

            fake.Replies["Retire"] = "{\"t\":7200,\"accepted\":true}";
            Assert.IsNull(svc.RetireAsync().GetAwaiter().GetResult());
            Assert.IsTrue(save.tournament.retired);
            Assert.IsTrue(save.tournament.finalSent);
            Assert.AreEqual(-1, save.tournament.pendingT, "the kept snapshot is dropped");
            Assert.IsFalse(svc.PrestigeLeavesRun);
            CollectionAssert.Contains(fake.Calls, "Retire");
        }

        [Test]
        public void RetireAsync_AlreadyRetiredElsewhere_CountsAsDone()
        {
            var svc = Spawn(prestigeCount: 3);
            var save = SaveManager.Instance.Current;
            var now = DateTime.UtcNow;
            save.tournament = new TournamentSaveData
            {
                week = PveRules.WeekId(now), startUtc = PveRules.Iso(now.AddHours(-2)), runEndsUtc = PveRules.Iso(now.AddHours(22)),
                prestigeCountAtJoin = 2,
            };
            var fake = new FakeTransport();
            fake.Replies["Retire"] = "{\"error\":\"retired\"}";
            PveApi.Transport = fake;
            Assert.IsNull(svc.RetireAsync().GetAwaiter().GetResult());
            Assert.IsTrue(save.tournament.retired);
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
