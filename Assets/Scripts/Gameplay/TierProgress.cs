using System;
using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Raises <see cref="PlayerProgressData.CurrentTier"/> as the player produces higher-tier items,
    /// and fires the tier-reached trigger when it moves.
    ///
    /// Nothing used to advance CurrentTier: the only writes were PrestigeSystem/PVPSystem resetting it
    /// to 1 and the dev console's "set tier". So it sat at 1 forever, which made both tier telemetry
    /// signals useless — the discrete <c>tier_reached</c> event had no caller at all, and the
    /// <c>highest_tier</c> dimension on every snapshot and prestige event was a hardcoded 1.
    ///
    /// "Reached" means produced: the highest <see cref="ItemSO.tier"/> the player has actually made this
    /// run (1 Subatomic → 5 Components), fed from both production paths — manual crafts
    /// (ManualCraftService) and automated recipe output (ProductionAchievementBridge). Collectors are not
    /// a source: they only yield tier-1 items, which is the floor anyway.
    /// </summary>
    public static class TierProgress
    {
        /// <summary>
        /// Call when an item has been produced. Raises CurrentTier if this item outranks it, and fires
        /// AchievementService.NotifyTierReached (which drives the ReachTier achievements and the
        /// tier_reached analytics event). Cheap no-op for the common case of an already-reached tier.
        /// Never throws — a telemetry/progress read must not be able to break production.
        /// </summary>
        public static void NotifyItemProduced(int itemId)
        {
            int tier = TierOf(itemId);
            if (tier <= 0) return;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;

            try
            {
                var em = world.EntityManager;
                using var q = em.CreateEntityQuery(ComponentType.ReadWrite<PlayerProgressData>());
                if (q.CalculateEntityCount() != 1) return;

                var progress = q.GetSingleton<PlayerProgressData>();
                if (tier <= progress.CurrentTier) return;

                progress.CurrentTier = tier;
                q.SetSingleton(progress);

                AchievementService.Instance?.NotifyTierReached(tier);
                GameLogger.Debug($"[TierProgress] Reached tier {tier} (item {itemId}).");
            }
            catch (Exception ex)
            {
                GameLogger.Debug($"[TierProgress] skipped: {ex.Message}");
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Test seam for the item→tier lookup. ItemDatabase fills its tables in Awake, and the test
        /// assemblies are editor-only (no MonoBehaviour lifecycle), so tests cannot populate it.
        /// </summary>
        internal static Func<int, int> TierLookupOverrideForTests;
#endif

        static int TierOf(int itemId)
        {
#if UNITY_EDITOR
            if (TierLookupOverrideForTests != null) return TierLookupOverrideForTests(itemId);
#endif
            return ItemDatabase.GetStatic(itemId)?.tier ?? 0;
        }
    }
}
