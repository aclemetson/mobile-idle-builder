using System;
using System.Collections.Generic;
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
            if (save == null || config == null)
            {
                GameLogger.Debug($"[Idle] CalculateAndApply skipped — save={save != null} config={config != null}");
                return null;
            }

            // Pay out every unlocked site's snapshot (inactive sites keep producing). The active
            // site's snapshot lives in both idleSnapshot and siteSnapshots[activeSiteIndex]; when
            // siteSnapshots is populated we iterate it exclusively to avoid double-counting.
            // Legacy/single-site saves fall back to the lone idleSnapshot.
            var snapshots = (save.siteSnapshots != null && save.siteSnapshots.Count > 0)
                ? save.siteSnapshots
                : new System.Collections.Generic.List<IdleCollectionSnapshot> { save.idleSnapshot };

            int chainCount = 0;
            foreach (var snap in snapshots)
                chainCount += snap?.chains?.Count ?? 0;
            GameLogger.Debug($"[Idle] CalculateAndApply — sites={snapshots.Count} chains={chainCount} lastSaved={save.lastSaved} applied={save.idleCollectionApplied} fromTimestamp={fromTimestamp}");

            // No chains saved across any site — nothing to simulate
            if (chainCount == 0)
            {
                GameLogger.Debug("[Idle] Skipped — no chains in any snapshot");
                return null;
            }

            // fromTimestamp overrides save.lastSaved so background-resume and cold-boot
            // paths both funnel through the same guard/calculation logic.
            string effectiveTimestamp = fromTimestamp ?? save.lastSaved;

            // Already applied for this departure timestamp — guard against double-apply
            if (!string.IsNullOrEmpty(save.idleCollectionApplied) &&
                save.idleCollectionApplied == effectiveTimestamp)
            {
                GameLogger.Debug($"[Idle] Skipped — already applied for timestamp {effectiveTimestamp}");
                return null;
            }

            // Brand-new game / no timestamp recorded yet
            if (string.IsNullOrEmpty(effectiveTimestamp))
            {
                GameLogger.Debug("[Idle] Skipped — no effective timestamp");
                return null;
            }

            if (!DateTime.TryParse(effectiveTimestamp, null, DateTimeStyles.RoundtripKind, out var saveTime))
            {
                GameLogger.Debug($"[Idle] Skipped — could not parse timestamp '{effectiveTimestamp}'");
                return null;
            }

            float elapsedSeconds = (float)(GameClock.UtcNow - saveTime.ToUniversalTime()).TotalSeconds;
            GameLogger.Debug($"[Idle] Elapsed={elapsedSeconds:F0}s effectiveTimestamp={effectiveTimestamp}");
            if (elapsedSeconds < 30f)
            {
                GameLogger.Debug($"[Idle] Skipped — elapsed {elapsedSeconds:F0}s < 30s minimum");
                return null;
            }

            float effectiveCap  = GetEffectiveIdleCap(config, upgrades);
            float cappedSeconds = Math.Min(elapsedSeconds, effectiveCap);
            float rate          = GetEffectiveCollectionRate(config, upgrades);

            // Timed boosts count only for the part of the time away they actually covered: a 30 min speed
            // burst pays 2x for 30 min of an 8 h absence, not all 8 h. The snapshot itself is unboosted.
            var departed = saveTime.ToUniversalTime();
            double weighted = BoostWeightedSeconds(
                cappedSeconds,
                SecondsLeftAt(save.speedBoostExpiryUtc, departed),   PremiumShopCalculator.SpeedBoostMultiplier,
                SecondsLeftAt(save.adsIdleBoostExpiryUtc, departed), AdRewardCalculator.IdleRateBoostMultiplier);
            GameLogger.Debug($"[Idle] Boost-weighted seconds={weighted:F0} of capped={cappedSeconds:F0}");

            var result = ComputePayout(snapshots, (float)weighted, rate);
            result.EntropyEarned = AdFree.ApplyIncome(save, result.EntropyEarned);   // +20% when ad-free (#503)
            result.ElapsedSeconds = elapsedSeconds;
            result.CappedSeconds  = cappedSeconds;
            result.MaxSeconds     = effectiveCap;

            ApplyResultToSave(save, result);

            // Stamp the departure timestamp so this exact session cannot be re-applied.
            // Uses effectiveTimestamp (not save.lastSaved) so both the cold-boot and
            // background-resume paths stamp the same key they checked above.
            save.idleCollectionApplied = effectiveTimestamp;

            GameLogger.Debug($"[Idle] Result — entropy={result.EntropyEarned} itemTypes={result.ItemsEarned.Count} elapsed={result.ElapsedSeconds:F0}s capped={result.CappedSeconds:F0}s");
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

        /// <summary>
        /// Multiplier applied to the effective collection rate while a rewarded-ad idle boost is active
        /// (1.5x), else 1. Applied AFTER the base-rate cap so the boost can push offline collection above
        /// the normal ceiling — that is the point of the reward.
        /// </summary>
        public static float GetAdIdleMultiplier(SaveData save)
            => PremiumShopCalculator.IsBoostActive(save?.adsIdleBoostExpiryUtc)
                ? AdRewardCalculator.IdleRateBoostMultiplier : 1f;

        /// <summary>Seconds a boost expiring at <paramref name="expiryIso"/> still had at <paramref name="at"/>; 0 if none.</summary>
        internal static double SecondsLeftAt(string expiryIso, DateTime at)
        {
            if (!BoostLock.TryParseUtc(expiryIso, out var end)) return 0d;
            return Math.Max(0d, (end - at).TotalSeconds);
        }

        /// <summary>
        /// Production-weighted length of the first <paramref name="cappedSeconds"/> after departure: each
        /// second counts <paramref name="speedMult"/> while the speed burst still ran (its first
        /// <paramref name="speedLeft"/> seconds) and <paramref name="idleMult"/> while the idle-rate ad
        /// boost ran (<paramref name="idleLeft"/>), multiplied where both overlap. With no boosts it equals
        /// <paramref name="cappedSeconds"/>.
        /// </summary>
        internal static double BoostWeightedSeconds(double cappedSeconds, double speedLeft, float speedMult,
                                                    double idleLeft, float idleMult)
        {
            if (cappedSeconds <= 0d) return 0d;
            double s = Math.Clamp(speedLeft, 0d, cappedSeconds);
            double i = Math.Clamp(idleLeft,  0d, cappedSeconds);
            double both = Math.Min(s, i);
            double speedOnly = s - both, idleOnly = i - both;
            double none = cappedSeconds - Math.Max(s, i);
            return both * speedMult * idleMult + speedOnly * speedMult + idleOnly * idleMult + none;
        }

        /// <summary>
        /// Computes an instant "Time Warp" payout: what <paramref name="seconds"/> of FULL production
        /// would yield (no idle collection-rate cut and no idle boost: a warp banks real production,
        /// 2026-11-20), from the latest captured snapshots. Does NOT mutate save and applies no elapsed/cap/idempotency guard — the
        /// caller applies the returned result (e.g. to the live ECS world). Returns null when
        /// there is no production to bank.
        /// </summary>
        public static IdleCollectionResult ComputeForDuration(
            SaveData save,
            GameConfigSO config,
            PersistentUpgradeService upgrades,
            float seconds)
        {
            if (save == null || config == null || seconds <= 0f) return null;

            var snapshots = (save.siteSnapshots != null && save.siteSnapshots.Count > 0)
                ? save.siteSnapshots
                : new System.Collections.Generic.List<IdleCollectionSnapshot> { save.idleSnapshot };

            var   result = ComputePayout(snapshots, seconds, 1f);
            result.EntropyEarned = AdFree.ApplyIncome(save, result.EntropyEarned);   // +20% when ad-free (#503)
            result.ElapsedSeconds = seconds;
            result.CappedSeconds  = seconds;
            return result.HasAnyOutput ? result : null;
        }

        /// <summary>
        /// Pure payout calculation: aggregates entropy + items produced over <paramref name="cappedSeconds"/>
        /// at <paramref name="rate"/> across every snapshot's chains. Does not mutate any save state.
        /// </summary>
        private static IdleCollectionResult ComputePayout(
            System.Collections.Generic.IEnumerable<IdleCollectionSnapshot> snapshots,
            float cappedSeconds,
            float rate)
        {
            var result = new IdleCollectionResult();
            foreach (var snap in snapshots)
            {
                if (snap?.chains == null) continue;
                foreach (var chain in snap.chains)
                {
                    int wholeItems = (int)Math.Floor(chain.itemsPerSecond * cappedSeconds * rate);
                    if (wholeItems <= 0) continue;

                    if (chain.endsAtEntropySink)
                    {
                        result.EntropyEarned += (long)(wholeItems * chain.baseSellValue);
                        if (!chain.notDelivered) // byproduct entropy and surplus sales pay but deliver nothing
                        {
                            result.DemonDelivered.TryGetValue(chain.itemId, out long delivered);
                            result.DemonDelivered[chain.itemId] = delivered + wholeItems;
                        }
                    }
                    else
                    {
                        result.ItemsEarned.TryGetValue(chain.itemId, out int existing);
                        result.ItemsEarned[chain.itemId] = existing + wholeItems;
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Applies a computed payout to SaveData.currentRun (entropy + inventory merge) and the world tasks.
        /// Returns how many items counted toward the tasks.
        /// </summary>
        public static long ApplyResultToSave(SaveData save, IdleCollectionResult result)
        {
            if (save?.currentRun == null || result == null) return 0;
            save.currentRun.baseCurrency += result.EntropyEarned;
            long counted = CreditDemonDeliveries(save, result);
            foreach (var kvp in result.ItemsEarned)
                MergeInventory(save.currentRun, kvp.Key, kvp.Value);
            return counted;
        }

        /// <summary>
        /// Credits what the Demon consumed while away to the world delivery tasks (Chemistry gate), at the same
        /// idle rate as the entropy. Every path that applies an idle result calls this exactly once. Returns how
        /// many items counted.
        /// </summary>
        public static long CreditDemonDeliveries(SaveData save, IdleCollectionResult result)
            => CreditDemonDeliveries(save, result?.DemonDelivered, "idle");

        /// <summary>
        /// Credits Demon deliveries from any bonus (offline return, Time Warp, Double Offline, Entropy Surge) to
        /// the world delivery tasks. Stages it finishes are queued for <see cref="WorldTaskService"/> to announce,
        /// as live deliveries are. Returns how many items counted toward the tasks.
        /// </summary>
        public static long CreditDemonDeliveries(SaveData save, IDictionary<int, long> deliveries, string source)
        {
            if (save == null || deliveries == null || deliveries.Count == 0) return 0;
            // The sets delivered in the world those deliveries came from (the live world's Demon).
            string here = WorldRules.ActiveWorld(save)?.id;
            var finished = new List<WorldTaskStage>();
            long counted = 0;
            foreach (var tasks in WorldTasks.All)
            {
                if (tasks == null || !WorldTasks.ShownIn(tasks, here) || WorldTasks.IsComplete(save, tasks)) continue;
                finished.AddRange(WorldTasks.CreditAll(save, tasks, deliveries, out long c));
                counted += c;
            }
            GameLogger.Info($"[WorldTasks] {source}: {counted} item(s) counted toward the tasks.");
            foreach (var stage in finished)
                GameLogger.Info($"[WorldTasks] Stage '{stage.id}' completed by {source}.");
            return counted;
        }

        static List<IdleCollectionSnapshot> Snapshots(SaveData save)
        {
            if (save == null) return new List<IdleCollectionSnapshot>();
            return (save.siteSnapshots != null && save.siteSnapshots.Count > 0)
                ? save.siteSnapshots
                : new List<IdleCollectionSnapshot> { save.idleSnapshot };
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
