using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Buying extra fields from the Build menu (Field Synthesis research). Each FieldSO carries its
    /// own price: <c>purchaseCostBase × purchaseCostMultiplier^n</c>, where n is how many times the
    /// player bought that field this run, so a field gets steeply dearer every time. A purchase places
    /// <c>purchaseGroupSize</c> fields side by side (quark fields always come as a pair).
    /// Counts live in <see cref="CurrentRunData"/> and reset with the run on prestige.
    /// </summary>
    public static class FieldPurchaseService
    {
        /// <summary>Raised after a purchase lands, so the Build menu can show the new price.</summary>
        public static event Action<FieldSO> OnFieldPurchased;

        public static bool IsForSale(FieldSO f) => f != null && f.purchaseCostBase > 0;

        public static bool IsAvailable(FieldSO f)
            => IsAvailable(f, id => ResearchService.Instance != null && ResearchService.Instance.IsUnlocked(id));

        public static bool IsAvailable(FieldSO f, Func<string, bool> isResearchUnlocked)
        {
            if (!IsForSale(f)) return false;
            if (string.IsNullOrEmpty(f.purchaseResearchId)) return true;
            return isResearchUnlocked != null && isResearchUnlocked(f.purchaseResearchId);
        }

        /// <summary>Price of the next purchase after <paramref name="purchased"/> earlier ones. Saturates at long.MaxValue.</summary>
        public static long PriceFor(FieldSO f, int purchased)
        {
            if (!IsForSale(f)) return 0;
            double price = f.purchaseCostBase * Math.Pow(Math.Max(1f, f.purchaseCostMultiplier), Math.Max(0, purchased));
            return price >= long.MaxValue ? long.MaxValue : (long)Math.Round(price);
        }

        public static long NextPrice(FieldSO f) => PriceFor(f, PurchasedCount(SaveManager.Instance?.Current, f?.id));

        public static int PurchasedCount(SaveData save, string fieldId)
        {
            var run = save?.currentRun;
            if (run?.fieldPurchaseIds == null || string.IsNullOrEmpty(fieldId)) return 0;
            int i = run.fieldPurchaseIds.IndexOf(fieldId);
            return i >= 0 && run.fieldPurchaseCounts != null && i < run.fieldPurchaseCounts.Count
                ? run.fieldPurchaseCounts[i] : 0;
        }

        internal static void RecordPurchase(SaveData save, string fieldId)
        {
            var run = save?.currentRun;
            if (run == null || string.IsNullOrEmpty(fieldId)) return;
            run.fieldPurchaseIds    ??= new List<string>();
            run.fieldPurchaseCounts ??= new List<int>();
            int i = run.fieldPurchaseIds.IndexOf(fieldId);
            if (i < 0)
            {
                run.fieldPurchaseIds.Add(fieldId);
                run.fieldPurchaseCounts.Add(1);
                return;
            }
            while (run.fieldPurchaseCounts.Count <= i) run.fieldPurchaseCounts.Add(0);
            run.fieldPurchaseCounts[i]++;
        }

        /// <summary>
        /// Charges the next price and places the field on every cell. The cells must already be
        /// checked as free. Returns false (and changes nothing) if the player can't afford it.
        /// </summary>
        public static bool TryPurchase(FieldSO f, FieldGenerator generator, IReadOnlyList<Vector2Int> cells)
        {
            var save = SaveManager.Instance?.Current;
            if (!IsAvailable(f) || generator == null || save == null || cells == null || cells.Count == 0)
                return false;

            long price = PriceFor(f, PurchasedCount(save, f.id));
            if (!TrySpendEntropy(price)) return false;

            foreach (var c in cells)
                generator.PlaceField(c.x, c.y, f);

            RecordPurchase(save, f.id);
            AchievementService.Instance?.NotifyEntropySpent(price);
            SaveManager.Instance.SaveLocal();
            GameLogger.Info($"[FieldPurchaseService] Bought {f.id} x{cells.Count} for {price}e.");
            OnFieldPurchased?.Invoke(f);
            return true;
        }

        private static bool TrySpendEntropy(long amount)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return false;
            using var query = world.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<PlayerProgressData>());
            if (query.IsEmpty) return false;

            var progress = query.GetSingleton<PlayerProgressData>();
            if (progress.BaseCurrency < amount) return false;
            progress.BaseCurrency      -= amount;
            progress.TotalEntropySpent += amount;
            query.SetSingleton(progress);
            return true;
        }
    }
}
