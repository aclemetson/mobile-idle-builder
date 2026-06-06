using System;
using System.Globalization;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Calculates what was collected during offline time and applies it to SaveData.
    /// Call CalculateAndApply() once per session open, before ECS loads.
    ///
    /// Idempotent: the idleCollectionApplied field guards against double-application
    /// (e.g. when cloud reconciliation swaps in a newer save and triggers a second load).
    /// </summary>
    public static class OfflineCollectionService
    {
        /// <summary>
        /// Computes offline earnings from save.idleSnapshot, merges them into save.currentRun,
        /// and returns a result for display. Returns null if there is nothing to show.
        /// </summary>
        public static IdleCollectionResult CalculateAndApply(
            SaveData save,
            GameConfigSO config,
            PersistentUpgradeService upgrades)
        {
            if (save == null || config == null) return null;

            // No chains saved — nothing to simulate
            if (save.idleSnapshot?.chains == null || save.idleSnapshot.chains.Count == 0) return null;

            // Already applied for this save timestamp — guard against double-apply
            if (!string.IsNullOrEmpty(save.idleCollectionApplied) &&
                save.idleCollectionApplied == save.lastSaved) return null;

            // Brand-new game — no prior session to earn from
            if (string.IsNullOrEmpty(save.lastSaved)) return null;

            if (!DateTime.TryParse(save.lastSaved, null, DateTimeStyles.RoundtripKind, out var saveTime))
                return null;

            float elapsedSeconds = (float)(DateTime.UtcNow - saveTime.ToUniversalTime()).TotalSeconds;
            if (elapsedSeconds < 30f) return null;

            float effectiveCap  = GetEffectiveIdleCap(config, upgrades);
            float cappedSeconds = Math.Min(elapsedSeconds, effectiveCap);
            float rate          = GetEffectiveCollectionRate(config, upgrades);

            var result = new IdleCollectionResult
            {
                ElapsedSeconds = elapsedSeconds,
                CappedSeconds  = cappedSeconds,
                MaxSeconds     = effectiveCap,
            };

            foreach (var chain in save.idleSnapshot.chains)
            {
                int wholeItems = (int)Math.Floor(chain.itemsPerSecond * cappedSeconds * rate);
                if (wholeItems <= 0) continue;

                if (chain.endsAtEntropySink)
                {
                    long entropy = (long)(wholeItems * chain.baseSellValue);
                    result.EntropyEarned += entropy;
                    save.currentRun.baseCurrency += entropy;
                }
                else
                {
                    MergeInventory(save.currentRun, chain.itemId, wholeItems);
                    if (result.ItemsEarned.TryGetValue(chain.itemId, out int existing))
                        result.ItemsEarned[chain.itemId] = existing + wholeItems;
                    else
                        result.ItemsEarned[chain.itemId] = wholeItems;
                }
            }

            // Stamp the save's own lastSaved so the same snapshot is never re-applied.
            // A newer save (different lastSaved) will pass the guard and run fresh.
            save.idleCollectionApplied = save.lastSaved;

            return result;
        }

        /// <summary>
        /// Effective idle cap in seconds: base + upgrade bonus, capped at absolute max.
        /// </summary>
        public static float GetEffectiveIdleCap(GameConfigSO config, PersistentUpgradeService upgrades)
        {
            float bonus = upgrades?.GetEffect(UpgradeEffectType.IdleTimeCap) ?? 0f;
            return Math.Min(config.idleAbsoluteMaxSeconds, config.idleBaseMaxSeconds + bonus);
        }

        /// <summary>
        /// Effective idle collection rate (0..1): base + upgrade bonus, capped at 1.
        /// </summary>
        public static float GetEffectiveCollectionRate(GameConfigSO config, PersistentUpgradeService upgrades)
        {
            float bonus = upgrades?.GetEffect(UpgradeEffectType.IdleCollectionRate) ?? 0f;
            return Math.Min(1f, config.idleBaseCollectionRate + bonus);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static void MergeInventory(CurrentRunData run, int itemId, int quantity)
        {
            run.inventoryKeys   ??= new System.Collections.Generic.List<string>();
            run.inventoryValues ??= new System.Collections.Generic.List<int>();

            string key = itemId.ToString();
            for (int i = 0; i < run.inventoryKeys.Count; i++)
            {
                if (run.inventoryKeys[i] == key)
                {
                    run.inventoryValues[i] += quantity;
                    return;
                }
            }
            run.inventoryKeys.Add(key);
            run.inventoryValues.Add(quantity);
        }
    }
}
