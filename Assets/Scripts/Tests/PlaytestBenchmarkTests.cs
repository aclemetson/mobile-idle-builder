using System;
using System.Collections.Generic;
using System.Linq;
using MobileIdleBuilder.Playtest;
using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    [TestFixture]
    public class PlaytestBenchmarkTests
    {
        private static PlaytestBenchmark.Probe Loaded() => new() { loaded = true, tutorialActive = true, tutorialStep = "intro_dialogue" };

        private static List<string> Types(PlaytestBenchmark.Probe prev, PlaytestBenchmark.Probe cur, HashSet<string> seen = null) =>
            PlaytestBenchmark.Detect(prev, cur, seen ?? new HashSet<string>()).Select(e => $"{e.type}:{e.detail}").ToList();

        [Test]
        public void Detect_NotLoaded_ReportsNothing()
        {
            Assert.IsEmpty(Types(Loaded(), new PlaytestBenchmark.Probe { loaded = false, entropy = 1_000_000 }));
        }

        [Test]
        public void Detect_TutorialStepChangeAndCompletion()
        {
            var prev = Loaded();
            var cur  = Loaded();
            cur.tutorialStep = "collect_first_electron";
            CollectionAssert.Contains(Types(prev, cur), "tutorial_step:collect_first_electron");

            var done = Loaded();
            done.tutorialActive = false;
            done.tutorialStep   = "hydrogen_loop_complete";
            var last = Loaded();
            last.tutorialStep = "hydrogen_loop_complete";
            CollectionAssert.Contains(Types(last, done), "tutorial_complete:hydrogen_loop_complete");
        }

        [Test]
        public void Detect_ResearchStartedAndDone()
        {
            var prev = Loaded();
            var cur  = Loaded();
            cur.activeResearch = "hydrogen_synthesis";
            cur.research.Add("recombination_i");
            var types = Types(prev, cur);
            CollectionAssert.Contains(types, "research_started:hydrogen_synthesis");
            CollectionAssert.Contains(types, "research_done:recombination_i");
        }

        [Test]
        public void Detect_EntropyThresholds_FireOncePerRun()
        {
            var seen = new HashSet<string>();
            var prev = Loaded();
            var cur  = Loaded();
            cur.entropy = 1_500;
            var first = Types(prev, cur, seen);
            CollectionAssert.Contains(first, "entropy:1e2");
            CollectionAssert.Contains(first, "entropy:1e3");
            CollectionAssert.DoesNotContain(first, "entropy:1e4");

            // Spent down and earned back: no repeat.
            var spent = Loaded(); spent.entropy = 50;
            var again = Loaded(); again.entropy = 2_000;
            Types(cur, spent, seen);
            CollectionAssert.DoesNotContain(Types(spent, again, seen), "entropy:1e3");
        }

        [Test]
        public void Detect_FirstBuildingOfTypeAndTotals()
        {
            var prev = Loaded();
            prev.buildings["maxwells_demon"] = 1;
            prev.buildingTotal = 1;
            var cur = Loaded();
            cur.buildings["maxwells_demon"] = 1;
            cur.buildings["quark_harvester"] = 4;
            cur.buildingTotal = 5;
            var types = Types(prev, cur);
            CollectionAssert.Contains(types, "building_first:quark_harvester");
            CollectionAssert.DoesNotContain(types, "building_first:maxwells_demon");
            CollectionAssert.Contains(types, "buildings_total:5");
        }

        [Test]
        public void Detect_CrystalsSpent_ReportsEachDrop()
        {
            var seen = new HashSet<string>();
            var a = Loaded(); a.crystals = 20_000;
            var b = Loaded(); b.crystals = 19_730;
            var c = Loaded(); c.crystals = 19_460;
            CollectionAssert.Contains(Types(a, b, seen), "crystals_spent:270");
            CollectionAssert.Contains(Types(b, c, seen), "crystals_spent:270");
        }

        [Test]
        public void Detect_PrestigeAndUnlocks()
        {
            var prev = Loaded();
            var cur  = Loaded();
            cur.prestigeAvailable = true;
            cur.prestigeRuns      = 1;
            cur.sitesUnlocked     = 1;
            var types = Types(prev, cur);
            CollectionAssert.Contains(types, "prestige_available:");
            CollectionAssert.Contains(types, "prestige:1");
            CollectionAssert.Contains(types, "site_unlocked:1");
        }

        [Test]
        public void FormatDuration_UsesHoursPastOneHour()
        {
            Assert.AreEqual("2m 05s", PlaytestBenchmark.FormatDuration(125));
            Assert.AreEqual("3h 20m", PlaytestBenchmark.FormatDuration(12_000));
        }

        [Test]
        public void GameClock_DevShift_MovesGameplayTimeOnly()
        {
            try
            {
                GameClock.DevReset();
                var before = GameClock.UtcNow;
                GameClock.DevShift(TimeSpan.FromHours(2));
                Assert.That((GameClock.UtcNow - before).TotalHours, Is.EqualTo(2).Within(0.01));
                Assert.That((GameClock.UtcNow - DateTime.UtcNow).TotalHours, Is.EqualTo(2).Within(0.01));
            }
            finally
            {
                GameClock.DevReset();
            }
            Assert.AreEqual(TimeSpan.Zero, GameClock.Offset);
        }
    }
}
