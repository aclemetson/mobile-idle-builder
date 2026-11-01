using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>Timing rules for the recipe-menu craft queue (#310). Timestamps are injected, never slept on.</summary>
    [TestFixture]
    public class CraftQueueTests
    {
        static readonly DateTime T0 = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

        static List<string> TickAt(CraftQueueSaveData q, DateTime now)
        {
            var done = new List<string>();
            CraftQueue.Tick(q, now, done.Add);
            return done;
        }

        [Test]
        public void Enqueue_OnEmptyQueue_StartsTheClock()
        {
            var q = new CraftQueueSaveData();
            CraftQueue.Enqueue(q, "proton", 3, 2f, T0);

            Assert.AreEqual(1, q.entries.Count);
            Assert.AreEqual(3, CraftQueue.TotalItems(q));
            Assert.AreEqual(0f, CraftQueue.ActiveProgress01(q, T0), 1e-4f);
            Assert.AreEqual(0.5f, CraftQueue.ActiveProgress01(q, T0.AddSeconds(1)), 1e-4f);
            Assert.AreEqual(6.0, CraftQueue.SecondsRemaining(q, T0), 1e-4);
        }

        [Test]
        public void Enqueue_SameRecipeAndSpeed_MergesIntoLastEntry()
        {
            var q = new CraftQueueSaveData();
            CraftQueue.Enqueue(q, "proton", 1, 1f, T0);
            CraftQueue.Enqueue(q, "proton", 5, 1f, T0);
            CraftQueue.Enqueue(q, "neutron", 2, 1f, T0);

            Assert.AreEqual(2, q.entries.Count);
            Assert.AreEqual(6, q.entries[0].count);
        }

        [Test]
        public void Tick_BeforeDue_FinishesNothing()
        {
            var q = new CraftQueueSaveData();
            CraftQueue.Enqueue(q, "proton", 1, 2f, T0);
            Assert.IsEmpty(TickAt(q, T0.AddSeconds(1.9)));
            Assert.AreEqual(1, CraftQueue.TotalItems(q));
        }

        [Test]
        public void Tick_FinishesItemsInOrder_AndCatchesUpAfterAGap()
        {
            var q = new CraftQueueSaveData();
            CraftQueue.Enqueue(q, "proton", 2, 1f, T0);
            CraftQueue.Enqueue(q, "neutron", 1, 4f, T0);

            // 1s + 1s + 4s = 6s: a 10s gap (app closed) finishes everything.
            var done = TickAt(q, T0.AddSeconds(10));
            CollectionAssert.AreEqual(new[] { "proton", "proton", "neutron" }, done);
            Assert.IsTrue(CraftQueue.IsEmpty(q));
            Assert.AreEqual("", q.activeCompleteUtc);
        }

        [Test]
        public void Tick_NextItemStartsWhenThePreviousFinished_NotWhenTicked()
        {
            var q = new CraftQueueSaveData();
            CraftQueue.Enqueue(q, "proton", 3, 2f, T0);

            // Ticked late at 3s: one item finished at 2s, the second started then and is half done.
            Assert.AreEqual(1, TickAt(q, T0.AddSeconds(3)).Count);
            Assert.AreEqual(0.5f, CraftQueue.ActiveProgress01(q, T0.AddSeconds(3)), 1e-4f);
            Assert.AreEqual(1, TickAt(q, T0.AddSeconds(4)).Count);
        }

        [Test]
        public void Cancel_FrontEntry_RestartsTheNextFromNow()
        {
            var q = new CraftQueueSaveData();
            CraftQueue.Enqueue(q, "proton", 4, 2f, T0);
            CraftQueue.Enqueue(q, "neutron", 1, 3f, T0);

            int removed = CraftQueue.Cancel(q, 0, T0.AddSeconds(1));
            Assert.AreEqual(4, removed, "cancelling returns every item the entry still held, the active one included");
            Assert.AreEqual("neutron", q.entries[0].recipeId);
            Assert.AreEqual(0f, CraftQueue.ActiveProgress01(q, T0.AddSeconds(1)), 1e-4f);
        }

        [Test]
        public void Cancel_LaterEntry_KeepsTheActiveItemsProgress()
        {
            var q = new CraftQueueSaveData();
            CraftQueue.Enqueue(q, "proton", 1, 2f, T0);
            CraftQueue.Enqueue(q, "neutron", 2, 3f, T0);

            Assert.AreEqual(2, CraftQueue.Cancel(q, 1, T0.AddSeconds(1)));
            Assert.AreEqual(0.5f, CraftQueue.ActiveProgress01(q, T0.AddSeconds(1)), 1e-4f);
        }

        [Test]
        public void Cancel_LastEntry_ClearsTheClock()
        {
            var q = new CraftQueueSaveData();
            CraftQueue.Enqueue(q, "proton", 1, 2f, T0);
            CraftQueue.Cancel(q, 0, T0);
            Assert.IsTrue(CraftQueue.IsEmpty(q));
            Assert.AreEqual("", q.activeCompleteUtc);
        }

        [Test]
        public void Cancel_BadIndex_ChangesNothing()
        {
            var q = new CraftQueueSaveData();
            CraftQueue.Enqueue(q, "proton", 1, 2f, T0);
            Assert.AreEqual(0, CraftQueue.Cancel(q, 5, T0));
            Assert.AreEqual(1, CraftQueue.TotalItems(q));
        }

        [Test]
        public void Tick_CorruptStamp_RestartsTheItemInsteadOfFinishingIt()
        {
            var q = new CraftQueueSaveData();
            CraftQueue.Enqueue(q, "proton", 1, 2f, T0);
            q.activeCompleteUtc = "not a date";

            Assert.IsEmpty(TickAt(q, T0.AddHours(1)));
            Assert.AreEqual(1, TickAt(q, T0.AddHours(1).AddSeconds(2)).Count);
        }

        // ── Save field ────────────────────────────────────────────────────────

        [Test]
        public void SaveData_RoundTripsTheQueue()
        {
            var save = new SaveData();
            CraftQueue.Enqueue(save.currentRun.craftQueue, "helium_4", 3, 2f, T0);

            var loaded = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(save));
            var q = loaded.currentRun.craftQueue;
            Assert.AreEqual(1, q.entries.Count);
            Assert.AreEqual("helium_4", q.entries[0].recipeId);
            Assert.AreEqual(3, q.entries[0].count);
            Assert.AreEqual(2f, q.entries[0].unitSeconds);
            Assert.AreEqual(1, TickAt(q, T0.AddSeconds(2)).Count, "the saved finish time survives the round trip");
        }

        [Test]
        public void SaveData_LegacyJsonWithoutTheQueue_LoadsAnEmptyQueue()
        {
            var loaded = JsonUtility.FromJson<SaveData>("{\"currentRun\":{\"baseCurrency\":5}}");
            Assert.IsNotNull(loaded.currentRun.craftQueue);
            Assert.IsTrue(CraftQueue.IsEmpty(loaded.currentRun.craftQueue));
        }

        [Test]
        public void Prestige_NewRunData_HasAnEmptyQueue()
        {
            Assert.IsTrue(CraftQueue.IsEmpty(new CurrentRunData().craftQueue));
        }

        [Test]
        public void FormatSeconds_ReadsNaturally()
        {
            Assert.AreEqual("0s", CraftQueueView.FormatSeconds(0));
            Assert.AreEqual("2s", CraftQueueView.FormatSeconds(1.2));
            Assert.AreEqual("1m 05s", CraftQueueView.FormatSeconds(65));
            Assert.AreEqual("2h 01m", CraftQueueView.FormatSeconds(7260));
        }
    }
}
