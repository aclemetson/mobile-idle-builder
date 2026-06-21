using System;
using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    [TestFixture]
    public class PremiumShopCalculatorTests
    {
        // ── Tier definition sanity ────────────────────────────────────────────

        [Test]
        public void AllSpeedUpTiers_HavePositiveCostAndDuration()
        {
            foreach (var t in PremiumShopCalculator.SpeedUpTiers)
            {
                Assert.Greater(t.CrystalCost, 0, $"{t.Name} cost");
                Assert.Greater(t.Duration.TotalSeconds, 0, $"{t.Name} duration");
            }
        }

        [Test]
        public void AllEntropyTiers_HavePositiveCostAndFloor()
        {
            foreach (var t in PremiumShopCalculator.EntropyTiers)
            {
                Assert.Greater(t.CrystalCost, 0, $"{t.Name} cost");
                Assert.Greater(t.MinimumFloor, 0, $"{t.Name} floor");
                Assert.Greater(t.NetWorthFraction, 0f, $"{t.Name} fraction");
            }
        }

        [Test]
        public void AllPrestigeTiers_HavePositiveCostFractionAndFloor()
        {
            foreach (var t in PremiumShopCalculator.PrestigeCurrencyTiers)
            {
                Assert.Greater(t.CrystalCost, 0, $"{t.Name} cost");
                Assert.Greater(t.PrestigeFraction, 0f, $"{t.Name} fraction");
                Assert.Greater(t.MinimumFloor, 0, $"{t.Name} floor");
            }
        }

        [Test]
        public void AllCrystalTiers_HaveProductIdAndAmount()
        {
            foreach (var t in PremiumShopCalculator.CrystalPackTiers)
            {
                Assert.IsFalse(string.IsNullOrEmpty(t.ProductId), $"{t.Name} productId");
                Assert.Greater(t.CrystalAmount, 0, $"{t.Name} amount");
            }
        }

        // ── IsBoostActive ─────────────────────────────────────────────────────

        [Test]
        public void IsBoostActive_WhenNull_ReturnsFalse()
            => Assert.IsFalse(PremiumShopCalculator.IsBoostActive(null));

        [Test]
        public void IsBoostActive_WhenEmpty_ReturnsFalse()
            => Assert.IsFalse(PremiumShopCalculator.IsBoostActive(""));

        [Test]
        public void IsBoostActive_WhenExpiryInFuture_ReturnsTrue()
        {
            string expiry = (DateTime.UtcNow + TimeSpan.FromHours(1)).ToString("O");
            Assert.IsTrue(PremiumShopCalculator.IsBoostActive(expiry));
        }

        [Test]
        public void IsBoostActive_WhenExpiryInPast_ReturnsFalse()
        {
            string expiry = (DateTime.UtcNow - TimeSpan.FromSeconds(1)).ToString("O");
            Assert.IsFalse(PremiumShopCalculator.IsBoostActive(expiry));
        }

        // ── GetSpeedBoostMultiplier ───────────────────────────────────────────

        [Test]
        public void GetSpeedBoostMultiplier_WhenActive_Returns2()
        {
            string expiry = (DateTime.UtcNow + TimeSpan.FromHours(1)).ToString("O");
            Assert.AreEqual(2f, PremiumShopCalculator.GetSpeedBoostMultiplier(expiry));
        }

        [Test]
        public void GetSpeedBoostMultiplier_WhenInactive_Returns1()
            => Assert.AreEqual(1f, PremiumShopCalculator.GetSpeedBoostMultiplier(null));

        // ── CalcSpeedBoostExpiry ──────────────────────────────────────────────

        [Test]
        public void CalcSpeedBoostExpiry_WhenNoActiveBoost_SetsExpiryFromNow()
        {
            var before = DateTime.UtcNow;
            string result = PremiumShopCalculator.CalcSpeedBoostExpiry(null, TimeSpan.FromHours(1));
            var expiry = DateTime.Parse(result, null, System.Globalization.DateTimeStyles.RoundtripKind);
            Assert.Greater(expiry, before + TimeSpan.FromMinutes(59));
            Assert.Less(expiry, before + TimeSpan.FromMinutes(61));
        }

        [Test]
        public void CalcSpeedBoostExpiry_WhenBoostActive_ExtendsExpiryByDuration()
        {
            var existingExpiry = DateTime.UtcNow + TimeSpan.FromHours(2);
            string result = PremiumShopCalculator.CalcSpeedBoostExpiry(
                existingExpiry.ToString("O"),
                TimeSpan.FromHours(1));

            var newExpiry = DateTime.Parse(result, null, System.Globalization.DateTimeStyles.RoundtripKind);
            // Should be ~3 hours from now (2 existing + 1 added)
            Assert.Greater(newExpiry, DateTime.UtcNow + TimeSpan.FromMinutes(179));
        }

        [Test]
        public void CalcSpeedBoostExpiry_WhenBoostExpired_SetsNewExpiryFromNow()
        {
            var pastExpiry = (DateTime.UtcNow - TimeSpan.FromHours(1)).ToString("O");
            var before = DateTime.UtcNow;
            string result = PremiumShopCalculator.CalcSpeedBoostExpiry(pastExpiry, TimeSpan.FromHours(1));
            var newExpiry = DateTime.Parse(result, null, System.Globalization.DateTimeStyles.RoundtripKind);
            Assert.Greater(newExpiry, before);
            Assert.Less(newExpiry, before + TimeSpan.FromHours(2));
        }

        // ── TryBuySpeedUp ─────────────────────────────────────────────────────

        [Test]
        public void SpeedUpPurchase_WithSufficientCrystals_ReducesBalance()
        {
            var result = PremiumShopCalculator.TryBuySpeedUp(0, 1000, null,
                out long newCrystals, out _);
            Assert.AreEqual(PurchaseResult.Success, result);
            Assert.AreEqual(1000 - PremiumShopCalculator.SpeedUpTiers[0].CrystalCost, newCrystals);
        }

        [Test]
        public void SpeedUpPurchase_WithInsufficientCrystals_ReturnsInsufficientCrystals()
        {
            var result = PremiumShopCalculator.TryBuySpeedUp(3, 10, null,
                out _, out _);
            Assert.AreEqual(PurchaseResult.InsufficientCrystals, result);
        }

        [Test]
        public void SpeedUpPurchase_SetsExpiryInFuture()
        {
            PremiumShopCalculator.TryBuySpeedUp(0, 10000, null,
                out _, out string expiry);
            Assert.IsTrue(PremiumShopCalculator.IsBoostActive(expiry));
        }

        // ── TryBuyEntropy ─────────────────────────────────────────────────────

        [Test]
        public void EntropyPurchase_WithSufficientCrystals_ReducesBalance()
        {
            var result = PremiumShopCalculator.TryBuyEntropy(0, 1000, 100000f,
                out long newCrystals, out _);
            Assert.AreEqual(PurchaseResult.Success, result);
            Assert.AreEqual(1000 - PremiumShopCalculator.EntropyTiers[0].CrystalCost, newCrystals);
        }

        [Test]
        public void EntropyPurchase_WithInsufficientCrystals_ReturnsInsufficientCrystals()
        {
            var result = PremiumShopCalculator.TryBuyEntropy(0, 5, 100000f,
                out _, out _);
            Assert.AreEqual(PurchaseResult.InsufficientCrystals, result);
        }

        [Test]
        public void CalcEntropyAmount_Tier0_Is10PercentOfNetWorth()
        {
            long amount = PremiumShopCalculator.CalcEntropyAmount(0, 100000f);
            Assert.AreEqual(10000L, amount); // 10% of 100000
        }

        [Test]
        public void CalcEntropyAmount_Tier0_RespectsFloor_WhenNetWorthLow()
        {
            long amount = PremiumShopCalculator.CalcEntropyAmount(0, 100f); // 10% = 10, below floor of 1000
            Assert.AreEqual(PremiumShopCalculator.EntropyTiers[0].MinimumFloor, amount);
        }

        [Test]
        public void CalcEntropyAmount_WhenNetWorthZero_UsesFloor()
        {
            long amount = PremiumShopCalculator.CalcEntropyAmount(0, 0f);
            Assert.AreEqual(PremiumShopCalculator.EntropyTiers[0].MinimumFloor, amount);
        }

        // ── TryBuyPrestigeCurrency ────────────────────────────────────────────

        // Default prestige config (matches PrestigeSystem fallbacks): pbase=5000, pscale=50.
        // At netWorth=50000: log10(50000/5000) x 50 = log10(10) x 50 = 50 prestige currency yield.
        const float Pbase = 5000f;
        const float Pscale = 50f;

        [Test]
        public void PrestigeCurrencyPurchase_WithSufficientCrystals_ReducesBalance()
        {
            var result = PremiumShopCalculator.TryBuyPrestigeCurrency(0, 5000, 50000f, Pbase, Pscale,
                out long newCrystals, out _);
            Assert.AreEqual(PurchaseResult.Success, result);
            Assert.AreEqual(5000 - PremiumShopCalculator.PrestigeCurrencyTiers[0].CrystalCost, newCrystals);
        }

        [Test]
        public void PrestigeCurrencyPurchase_AllTiers_ScaleWithPrestigeYield()
        {
            // yieldNow = 50; tier fractions 0.5/1.0/2.5/5.0 -> 25/50/125/250.
            long[] expected = { 25, 50, 125, 250 };
            for (int i = 0; i < PremiumShopCalculator.PrestigeCurrencyTiers.Length; i++)
            {
                PremiumShopCalculator.TryBuyPrestigeCurrency(i, 100000, 50000f, Pbase, Pscale,
                    out _, out long granted);
                Assert.AreEqual(expected[i], granted, $"Tier {i}");
            }
        }

        [Test]
        public void CalcPrestigeCurrencyAmount_WhenYieldZero_UsesFloor()
        {
            // netWorth at/below pbase -> log10 <= 0 -> yield 0 -> floor applies.
            for (int i = 0; i < PremiumShopCalculator.PrestigeCurrencyTiers.Length; i++)
            {
                long amount = PremiumShopCalculator.CalcPrestigeCurrencyAmount(i, Pbase, Pbase, Pscale);
                Assert.AreEqual(PremiumShopCalculator.PrestigeCurrencyTiers[i].MinimumFloor, amount, $"Tier {i}");
            }
        }
    }
}
