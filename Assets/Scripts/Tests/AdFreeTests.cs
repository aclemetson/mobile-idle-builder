using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// #503: the ad-free purchase keeps every ad reward (the Surge included) without the ad and adds a permanent
    /// +20% to entropy income, live (on top of the Cyclotron boost) and offline.
    /// </summary>
    public class AdFreeTests
    {
        [Test]
        public void NotOwned_ChangesNothing()
        {
            var save = new SaveData();
            Assert.IsFalse(AdFree.Owned(save));
            Assert.AreEqual(1f, AdFree.IncomeMultiplier(save));
            Assert.AreEqual(1_000L, AdFree.ApplyIncome(save, 1_000));
            Assert.AreEqual(2.5f, AdFree.CombinedIncomeMultiplier(save, 2.5f));
            Assert.IsFalse(AdFree.Owned(null));
        }

        [Test]
        public void Owned_AddsTwentyPercent_OnTopOfTheCyclotron()
        {
            var save = new SaveData { adFree = true };
            Assert.AreEqual(1.2f, AdFree.IncomeMultiplier(save), 1e-6f);
            Assert.AreEqual(1_200L, AdFree.ApplyIncome(save, 1_000));
            Assert.AreEqual(0L, AdFree.ApplyIncome(save, 0));
            Assert.AreEqual(1.2f, AdFree.CombinedIncomeMultiplier(save, 1f), 1e-6f);
            Assert.AreEqual(3.6f, AdFree.CombinedIncomeMultiplier(save, 3f), 1e-5f, "multiplies the Cyclotron boost");
            Assert.AreEqual(1.2f, AdFree.CombinedIncomeMultiplier(save, 0f), 1e-6f, "no boost written yet counts as 1");
        }

        [Test]
        public void Grant_OnlyOnce_AndFiresTheEvent()
        {
            var save = new SaveData();
            int fired = 0;
            void OnGranted() => fired++;
            AdFree.Granted += OnGranted;
            try
            {
                Assert.IsTrue(AdFree.Grant(save));
                Assert.IsFalse(AdFree.Grant(save), "a restore of an owned save is a no-op");
                Assert.IsFalse(AdFree.Grant(null));
            }
            finally { AdFree.Granted -= OnGranted; }
            Assert.IsTrue(save.adFree);
            Assert.AreEqual(1, fired);
        }

        [Test]
        public void Ownership_SurvivesASave_AndPrestige()
        {
            var save = new SaveData { adFree = true };
            var loaded = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(save));
            Assert.IsTrue(loaded.adFree);
            loaded.currentRun = new CurrentRunData();   // what prestige does
            Assert.IsTrue(AdFree.Owned(loaded));
        }

        [Test]
        public void OfflinePayout_GetsTheBonus()
        {
            var save = new SaveData { adFree = true };
            save.idleSnapshot = new IdleCollectionSnapshot { chains = new List<IdleChainEntry>
            {
                new IdleChainEntry { itemId = 6, itemsPerSecond = 1f, endsAtEntropySink = true, baseSellValue = 10f },
            }};
            var config = ScriptableObject.CreateInstance<GameConfigSO>();
            try
            {
                var paid = OfflineCollectionService.ComputeForDuration(save, config, null, 100f);
                Assert.AreEqual(1_200L, paid.EntropyEarned, "100 s x 1/s x 10e = 1,000, +20%");
                save.adFree = false;
                Assert.AreEqual(1_000L, OfflineCollectionService.ComputeForDuration(save, config, null, 100f).EntropyEarned);
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void SurgeCard_SaysClaim_WhenAdFree()
        {
            var def = AdRewardCalculator.Def(AdPlacement.EntropySurge);
            StringAssert.StartsWith("Claim +60,000 ◈ entropy (80%)", EntropySurgeOfferController.MessageText(60_000, def, adFree: true));
            StringAssert.StartsWith("Watch a short ad for", EntropySurgeOfferController.MessageText(60_000, def));
        }
    }
}
