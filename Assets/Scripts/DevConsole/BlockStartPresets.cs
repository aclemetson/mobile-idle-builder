#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MobileIdleBuilder.Dev
{
    /// <summary>
    /// Builds a save for "a player who has just reached research block X for the first time", so the dev
    /// console's `block load` can drop a tester at the start of any game phase.
    ///
    /// The state comes from game_data.json research_blocks (start_* fields, filled by scripts/block_starts.py):
    /// every research in earlier blocks unlocked, the block's expected prestige count and permanent-upgrade
    /// levels (with their multipliers), the worlds and sites that research opens, and the starting entropy.
    /// The run itself is reset the way a prestige resets it: an empty grid that the game rebuilds on load.
    /// Persistent things that are not progression (achievements, crystals, managers, settings) are kept.
    /// </summary>
    public static class BlockStartPresets
    {
        public const string CapturePrefix = "block_";

        /// <summary>Finds a block by id, display name, or 1-based order number.</summary>
        public static ResearchBlock Find(ResearchDatabaseSO db, string key)
        {
            if (db?.blocks == null || string.IsNullOrEmpty(key)) return null;
            var ordered = db.blocks.OrderBy(b => b.order).ToList();
            if (int.TryParse(key, out int n) && n >= 1 && n <= ordered.Count) return ordered[n - 1];
            string k = key.Trim().ToLowerInvariant().Replace(' ', '_');
            return ordered.FirstOrDefault(b => b.id == k || (b.displayName ?? "").ToLowerInvariant().Replace(' ', '_') == k);
        }

        /// <summary>Research ids in blocks that come before <paramref name="block"/>.</summary>
        public static List<string> ResearchBefore(ResearchDatabaseSO db, ResearchBlock block)
        {
            var earlier = new HashSet<string>(db.blocks.Where(b => b.order < block.order).Select(b => b.id));
            return db.allResearch.Where(r => r != null && earlier.Contains(r.block)).Select(r => r.id).ToList();
        }

        /// <summary>Upgrade levels from the save format ("id:level").</summary>
        public static Dictionary<string, int> ParseLevels(IEnumerable<string> entries)
        {
            var levels = new Dictionary<string, int>();
            foreach (var e in entries ?? Enumerable.Empty<string>())
            {
                int colon = e.LastIndexOf(':');
                if (colon > 0 && int.TryParse(e.Substring(colon + 1), out int lvl) && lvl > 0)
                    levels[e.Substring(0, colon)] = lvl;
            }
            return levels;
        }

        static float Effect(Dictionary<string, int> levels, UpgradeEffectType type) =>
            PersistentUpgradeService.All.Where(d => d.effectType == type)
                                        .Sum(d => d.effectPerLevel * (levels.TryGetValue(d.id, out int l) ? l : 0));

        /// <summary>Prestige currency spent buying these levels (each level's listed cost).</summary>
        public static long CurrencySpent(Dictionary<string, int> levels)
        {
            long spent = 0;
            foreach (var kv in levels)
            {
                var def = PersistentUpgradeService.FindDef(kv.Key);
                if (def == null) continue;
                for (int i = 0; i < kv.Value && i < def.levelCosts.Length; i++) spent += def.levelCosts[i];
            }
            return spent;
        }

        /// <summary>
        /// The save for the start of <paramref name="block"/>, built from <paramref name="current"/> (which is
        /// not modified). The first block (the tutorial) gives a fresh game that keeps only the player id.
        /// </summary>
        public static SaveData Build(SaveData current, ResearchDatabaseSO db, WorldDatabaseSO worlds, ResearchBlock block)
        {
            if (block == null || db == null) return null;
            var first = db.blocks.OrderBy(b => b.order).First();
            if (block == first)
                return new SaveData { playerId = current?.playerId, devConsoleUsed = true };

            var save = current != null ? JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(current)) : new SaveData();

            // The run, reset the way a prestige resets it.
            save.currentRun = new CurrentRunData();
            PrestigeSaveWatcher.ResetSitesForPrestige(save);
            save.activeResearchId          = null;
            save.activeResearchCompleteUtc = null;
            save.speedBoostExpiryUtc       = null;
            save.tutorial                  = new TutorialSaveData { currentStepId = "", isActive = false, hasCompletedFirstRun = true };

            // Progression: everything before this block is researched.
            save.unlockedResearch = ResearchBefore(db, block);

            // Meta: the prestiges and permanent upgrades a player typically has on first arrival.
            var levels = ParseLevels(block.startPermanentUpgrades);
            save.prestigeCount            = block.startPrestigeCount;
            save.permanentUpgrades        = levels.Select(kv => $"{kv.Key}:{kv.Value}").ToList();
            // prestigeCurrency is the lifetime total earned; what the player holds is total minus spent.
            save.prestigeCurrencySpent    = CurrencySpent(levels);
            save.prestigeCurrency         = System.Math.Max(block.startPrestigeCurrencyEarned, save.prestigeCurrencySpent);
            save.prestigeSpeedMultiplier  = 1f + Effect(levels, UpgradeEffectType.CraftSpeedMultiplier);
            save.prestigeOutputMultiplier = 1f + Effect(levels, UpgradeEffectType.OutputQuantityMultiplier);
            save.prestigeCostReduction    = Effect(levels, UpgradeEffectType.BuildingCostReduction);

            // Starting entropy: enough for the block's first research, plus Entropy Headstart, which the
            // prestige reset would have granted.
            save.currentRun.baseCurrency = block.startEntropy + (long)Effect(levels, UpgradeEffectType.StartingEntropyBonus);
            save.lastKnownNetWorth       = save.currentRun.baseCurrency;

            // Sites and worlds opened by earlier research (unlocks survive prestige, so a returning player has them).
            var researched = new HashSet<string>(save.unlockedResearch);
            var sites  = new HashSet<string>(block.startSites ?? System.Array.Empty<string>());
            var opened = new List<string>();
            foreach (var w in worlds?.allWorlds ?? System.Array.Empty<WorldSO>())
            {
                if (w == null || w == worlds.allWorlds[0]) continue;               // world 0 (Physics) is implicit
                if (w.prereqUnlockIds == null || w.prereqUnlockIds.Count == 0) continue;
                if (!w.prereqUnlockIds.All(researched.Contains)) continue;
                opened.Add(w.id);
                foreach (var s in w.siteIds ?? new List<string>()) sites.Add(s);
            }
            save.unlockedWorlds   = opened;
            save.unlockedSites    = sites.ToList();
            save.devConsoleUsed   = true;
            return save;
        }
    }
}
#endif
