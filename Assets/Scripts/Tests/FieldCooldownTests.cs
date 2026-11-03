using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// EditMode tests for the field tap-cooldown system:
    ///   • FieldCooldownCalculator (pure math)
    ///   • the "Quick Hands" prestige upgrade feeding the calculator via PersistentUpgradeService
    ///   • ManualFieldCollector.PickWeightedItem (one item per tap)
    /// </summary>
    [TestFixture]
    public class FieldCooldownTests
    {
        // ── FieldCooldownCalculator ──────────────────────────────────────────

        [Test]
        public void Effective_NoUpgrades_ReturnsBase()
        {
            Assert.AreEqual(1.5f, FieldCooldownCalculator.Effective(1.5f, 1f, 0f), 0.0001f);
        }

        [Test]
        public void Effective_ResearchMultiplier_ReducesCooldown()
        {
            // Rapid Extraction I (0.85) × II (0.80) = 0.68 → 1.5 × 0.68 = 1.02
            float mult = 0.85f * 0.80f;
            Assert.AreEqual(1.5f * mult, FieldCooldownCalculator.Effective(1.5f, mult, 0f), 0.0001f);
        }

        [Test]
        public void Effective_PrestigeReduction_StacksMultiplicativelyWithResearch()
        {
            // base 2.0 × research 0.5 × (1 − 0.5 prestige) = 0.5
            Assert.AreEqual(0.5f, FieldCooldownCalculator.Effective(2.0f, 0.5f, 0.5f), 0.0001f);
        }

        [Test]
        public void Effective_ClampedToFloor()
        {
            // Extreme reductions never drop below the 0.25 s floor.
            float v = FieldCooldownCalculator.Effective(1.5f, 0.01f, 0.99f);
            Assert.AreEqual(FieldCooldownCalculator.MinCooldownSeconds, v, 0.0001f);
        }

        [Test]
        public void Effective_ResearchMultiplierAboveOne_IsClampedToBase()
        {
            // A bogus >1 multiplier can never make the cooldown longer than the base.
            Assert.AreEqual(1.5f, FieldCooldownCalculator.Effective(1.5f, 2f, 0f), 0.0001f);
        }

        // ── Quick Hands prestige upgrade → calculator ────────────────────────

        [Test]
        public void QuickHands_ReducesEffectiveCooldown()
        {
            var go  = new GameObject("TestUpgradeSvc");
            var svc = go.AddComponent<PersistentUpgradeService>();
            try
            {
                // Max level: 10 × 5% = 50% reduction.
                svc.LoadFromSave(new List<string> { "field_cooldown:10" });
                float reduction = svc.GetEffect(UpgradeEffectType.FieldCooldownReduction);
                Assert.AreEqual(0.5f, reduction, 0.0001f, "10 levels × 5%");

                float effective = FieldCooldownCalculator.Effective(1.5f, 1f, reduction);
                Assert.AreEqual(0.75f, effective, 0.0001f, "1.5 × (1 − 0.5)");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ── PickWeightedItem ─────────────────────────────────────────────────

        [Test]
        public void PickWeightedItem_SingleDrop_AlwaysReturnsThatItem()
        {
            var item  = ScriptableObject.CreateInstance<ItemSO>();
            var field = ScriptableObject.CreateInstance<FieldSO>();
            field.drops = new List<FieldDropEntry> { new FieldDropEntry { item = item, weight = 1f } };
            try
            {
                Assert.AreSame(item, ManualFieldCollector.PickWeightedItem(field));
            }
            finally
            {
                Object.DestroyImmediate(field);
                Object.DestroyImmediate(item);
            }
        }

        [Test]
        public void PickWeightedItem_MultipleDrops_ReturnsOneOfTheDrops()
        {
            var a = ScriptableObject.CreateInstance<ItemSO>();
            var b = ScriptableObject.CreateInstance<ItemSO>();
            var field = ScriptableObject.CreateInstance<FieldSO>();
            field.drops = new List<FieldDropEntry>
            {
                new FieldDropEntry { item = a, weight = 0.5f },
                new FieldDropEntry { item = b, weight = 0.5f },
            };
            try
            {
                for (int i = 0; i < 20; i++)
                {
                    var picked = ManualFieldCollector.PickWeightedItem(field);
                    Assert.IsTrue(picked == a || picked == b, "picked item must be one of the configured drops");
                }
            }
            finally
            {
                Object.DestroyImmediate(field);
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
            }
        }
    }
}
