using System;
using System.Collections.Generic;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Ascension (Item Timeline phase 4, 2026-11-23): once every research in the game is unlocked in one run, the
    /// player can Ascend. It is a prestige that also locks the other worlds again, starts their delivery stages over
    /// and spends the prestige currency held, keeping the prestige shop's upgrades, managers, the megastructure and
    /// its material. Each Ascension pays megastructure material (more each time); material buys Megastructure
    /// Expansion levels, each +10% output for good, at a cost that grows faster than the payout, so the loop runs for
    /// years (docs/item-timeline.html, "Ascension and the megastructure").
    /// Pure rules on <see cref="SaveData"/>; <see cref="PrestigeSystem"/> applies them, the Megastructure panel asks.
    /// </summary>
    public static class AscensionRules
    {
        public const double MaterialFirst   = 10;
        public const double MaterialGrowth  = 1.18;
        public const double StageCostFirst  = 10;
        public const double StageCostGrowth = 1.55;
        public const float  BonusPerLevel   = 0.10f;

        /// <summary>Set by the Ascend button just before it requests the prestige; PrestigeSystem consumes it.</summary>
        public static bool Pending;

        /// <summary>Material the next Ascension pays, after <paramref name="ascensions"/> earlier ones.</summary>
        public static long MaterialFor(int ascensions) =>
            (long)Math.Round(MaterialFirst * Math.Pow(MaterialGrowth, Math.Max(0, ascensions)));

        /// <summary>Material for Expansion level <paramref name="level"/> + 1.</summary>
        public static long ExpansionCost(int level) =>
            (long)Math.Round(StageCostFirst * Math.Pow(StageCostGrowth, Math.Max(0, level)));

        /// <summary>Extra output from <paramref name="level"/> Expansion levels (0.10 = +10%), compounding.</summary>
        public static float ExpansionBonus(int level) => (float)Math.Pow(1 + BonusPerLevel, Math.Max(0, level)) - 1f;

        /// <summary>True when every research id in <paramref name="allResearchIds"/> is unlocked in this run.</summary>
        public static bool IsAvailable(SaveData save, IEnumerable<string> allResearchIds)
        {
            if (save?.unlockedResearch == null || allResearchIds == null) return false;
            var have = new HashSet<string>(save.unlockedResearch);
            bool any = false;
            foreach (var id in allResearchIds)
            {
                if (string.IsNullOrEmpty(id)) continue;
                any = true;
                if (!have.Contains(id)) return false;
            }
            return any;
        }

        /// <summary>
        /// The Ascension's own resets on top of the prestige's (call after PrestigeRunReset.ResetRun). Returns the
        /// material it paid.
        /// </summary>
        public static long Apply(SaveData save)
        {
            if (save == null) return 0;
            long paid = MaterialFor(save.ascensionCount);
            save.megastructureMaterial += paid;
            save.ascensionCount++;
            save.unlockedWorlds?.Clear();
            save.worldTaskStage = 0;
            save.worldTaskProgressKeys?.Clear();
            save.worldTaskProgressValues?.Clear();
            save.worldTaskSets?.Clear();
            save.prestigeCurrency = save.prestigeCurrencySpent;   // the shop keeps its upgrades; what was held is spent
            return paid;
        }

        /// <summary>Buys the next Expansion level with material. False when there is not enough.</summary>
        public static bool BuyExpansion(SaveData save)
        {
            if (save == null) return false;
            long cost = ExpansionCost(save.megastructureExpansion);
            if (save.megastructureMaterial < cost) return false;
            save.megastructureMaterial -= cost;
            save.megastructureExpansion++;
            return true;
        }
    }
}
