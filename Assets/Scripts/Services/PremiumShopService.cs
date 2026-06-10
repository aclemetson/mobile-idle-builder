using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Owns all premium-shop purchase logic and the active speed-boost state.
    /// Persists across scenes; wire up in SplashScene or GameScene alongside SaveManager.
    /// </summary>
    public class PremiumShopService : SingletonMonoBehaviour<PremiumShopService>
    {
        protected override bool PersistAcrossScenes => true;

        // ── Public read-only helpers ──────────────────────────────────────────

        public bool IsSpeedBoostActive()
            => PremiumShopCalculator.IsBoostActive(SaveManager.Instance?.Current?.speedBoostExpiryUtc);

        public float GetSpeedBoostMultiplier()
            => PremiumShopCalculator.GetSpeedBoostMultiplier(SaveManager.Instance?.Current?.speedBoostExpiryUtc);

        // ── Purchase methods ──────────────────────────────────────────────────

        public PurchaseResult TryBuySpeedUp(int tierIndex)
        {
            var save = SaveManager.Instance?.Current;
            if (save == null) return PurchaseResult.InvalidTier;

            var result = PremiumShopCalculator.TryBuySpeedUp(
                tierIndex,
                save.paidCurrency,
                save.speedBoostExpiryUtc,
                out long newCrystals,
                out string newExpiry);

            if (result != PurchaseResult.Success) return result;

            save.paidCurrency        = newCrystals;
            save.speedBoostExpiryUtc = newExpiry;
            SaveManager.Instance.SaveLocal();
            return PurchaseResult.Success;
        }

        public PurchaseResult TryBuyEntropy(int tierIndex)
        {
            var save = SaveManager.Instance?.Current;
            if (save == null) return PurchaseResult.InvalidTier;

            var result = PremiumShopCalculator.TryBuyEntropy(
                tierIndex,
                save.paidCurrency,
                save.lastKnownNetWorth,
                out long newCrystals,
                out long entropyGranted);

            if (result != PurchaseResult.Success) return result;

            save.paidCurrency             = newCrystals;
            save.currentRun.baseCurrency += entropyGranted;

            // If ECS is live (game scene), write through to the ECS singleton
            ApplyEntropyToECS(entropyGranted);

            SaveManager.Instance.SaveLocal();
            return PurchaseResult.Success;
        }

        public PurchaseResult TryBuyPrestigeCurrency(int tierIndex)
        {
            var save = SaveManager.Instance?.Current;
            if (save == null) return PurchaseResult.InvalidTier;

            var result = PremiumShopCalculator.TryBuyPrestigeCurrency(
                tierIndex,
                save.paidCurrency,
                out long newCrystals,
                out long pcGranted);

            if (result != PurchaseResult.Success) return result;

            save.paidCurrency      = newCrystals;
            save.prestigeCurrency += pcGranted;

            // Write through to ECS if live
            ApplyPrestigeCurrencyToECS(pcGranted);

            SaveManager.Instance.SaveLocal();
            return PurchaseResult.Success;
        }

        /// <summary>Called by IAPService after a successful crystal purchase.</summary>
        public void AwardCrystals(long amount)
        {
            var save = SaveManager.Instance?.Current;
            if (save == null) return;

            save.paidCurrency     += amount;
            save.crystalsPurchased += amount;
            SaveManager.Instance.SaveLocal();
        }

        // ── ECS write-through (optional — only applies if ECS world is loaded) ──

        private void ApplyEntropyToECS(long amount)
        {
            var bridge = ECSLoadBridge.Instance;
            if (bridge == null || !bridge.IsLoaded) return;
            bridge.AddEntropy(amount);
        }

        private void ApplyPrestigeCurrencyToECS(long amount)
        {
            var bridge = ECSLoadBridge.Instance;
            if (bridge == null || !bridge.IsLoaded) return;
            bridge.AddPrestigeCurrency(amount);
        }
    }
}
