using System;
using System.Globalization;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Calculates what was collected during offline time and applies it to SaveData.
    /// Call CalculateAndApply() once per session open, before ECS loads.
    ///
    /// Idempotent: the idleCollectionApplied field guards against double-application
    /// (e.g. when cloud reconciliation swaps in a newer save and triggers a second load,
    /// or when the background→foreground path and the cold-boot path both fire).
    /// </summary>
    public static class OfflineCollectionService
    {
        /// <summary>
        /// Computes offline earnings from save.idleSnapshot, merges them into save.currentRun,
        /// and returns a result for display. Returns null if there is nothing to show.
        ///
        /// <paramref name="fromTimestamp"/> lets the caller supply the exact "left at" UTC
        /// timestamp (e.g. from PlayerPrefs) instead of relying on save.lastSaved.
        /// Falls back to save.lastSaved when null.
        /// </summary>
        public static IdleCollectionResult CalculateAndApply(
            SaveData save,
            GameConfigSO config,
            PersistentUpgradeService upgrades,
            string fromTimestamp = null)
        {
            if (save == null || config == null) return null;

            // No chains saved — nothing to simulate
            if (save.idleSnapshot?.chains == null || save.idleSnapshot.chains.Count == 0) return null;

            // fromTimestamp overrides save.lastSaved so background-resume and cold-boot
            // paths both funnel through the same guard/calculation logic.
            string effectiveTimestamp = fromTimestamp ?? save.lastSaved;

            // Already applied for this departure timestamp — guard against double-apply
            if (!string.IsNullOrEmpty(save.idleCollectionApplied) &&
                save.idleCollectionApplied == effectiveTimestamp) return null;

            // Brand-new game / no timestamp recorded yet
            if (string.IsNullOrEmpty(effectiveTimestamp)) return null;

            if (!DateTime.TryParse(effectiveTimestamp, null, DateTimeStyles.RoundtripKind, out var saveTime))
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

            // Stamp the departure timestamp so this exact session cannot be re-applied.
            // Uses effectiveTimestamp (not save.lastSaved) so both the cold-boot and
            // background-resume paths stamp the same key they checked above.
            save.idleCollectionApplied = effectiveTimestamp;

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
