using System;
using System.Collections.Generic;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Defines one permanent upgrade option purchasable with prestige currency.
    /// </summary>
    public class UpgradeDef
    {
        public readonly string              id;
        public readonly string              displayName;
        public readonly string              description;
        public readonly UpgradeEffectType   effectType;
        public readonly float               effectPerLevel; // additive per level
        public readonly int                 maxLevel;
        public readonly int[]               levelCosts;     // prestige currency cost per level (index 0 = level 1)
        public readonly (string id, int minLevel)[] prereqs;

        public UpgradeDef(string id, string displayName, string description,
            UpgradeEffectType effectType, float effectPerLevel, int maxLevel,
            int[] levelCosts, (string, int)[] prereqs = null)
        {
            this.id             = id;
            this.displayName    = displayName;
            this.description    = description;
            this.effectType     = effectType;
            this.effectPerLevel = effectPerLevel;
            this.maxLevel       = maxLevel;
            this.levelCosts     = levelCosts;
            this.prereqs        = prereqs ?? Array.Empty<(string, int)>();
        }
    }

    /// <summary>
    /// Manages permanent upgrades that persist between prestiges.
    /// Defines the upgrade catalogue, tracks purchased levels, and computes aggregate effects.
    /// </summary>
    public class PersistentUpgradeService : SingletonMonoBehaviour<PersistentUpgradeService>
    {
        protected override bool PersistAcrossScenes => false;

        public event Action OnUpgradePurchased;

        // ── Upgrade catalogue ─────────────────────────────────────────────────

        public static readonly UpgradeDef[] All = new UpgradeDef[]
        {
            // ── Tier 1: no prerequisites ──────────────────────────────────────
            new UpgradeDef(
                "entropy_headstart", "Entropy Headstart",
                "Start each run with +250 extra entropy per level.",
                UpgradeEffectType.StartingEntropyBonus, 250f, 5,
                new[]{ 5, 10, 20, 40, 80 }),

            new UpgradeDef(
                "memory_resonance", "Memory Resonance",
                "All research costs are 5% cheaper per level on every run.",
                UpgradeEffectType.GlobalResearchDiscount, 0.05f, 5,
                new[]{ 10, 20, 40, 80, 160 }),

            new UpgradeDef(
                "assembly_line", "Assembly Line",
                "All buildings craft 10% faster per level.",
                UpgradeEffectType.CraftSpeedMultiplier, 0.10f, 5,
                new[]{ 15, 30, 60, 120, 240 }),

            new UpgradeDef(
                "expanded_vault", "Expanded Vault",
                "Increase inventory capacity by +20 slots per level.",
                UpgradeEffectType.VaultCapacity, 20f, 5,
                new[]{ 25, 38, 56, 84, 126 }),

            // ── Tier 2: require 1 Tier-1 level ───────────────────────────────
            new UpgradeDef(
                "quantum_yield", "Quantum Yield",
                "All recipes produce +10% more output per level.",
                UpgradeEffectType.OutputQuantityMultiplier, 0.10f, 5,
                new[]{ 40, 80, 160, 320, 640 },
                new[]{ ("assembly_line", 1) }),

            new UpgradeDef(
                "efficient_layouts", "Efficient Layouts",
                "Building placement costs 5% less per level (max −30%).",
                UpgradeEffectType.BuildingCostReduction, 0.05f, 6,
                new[]{ 35, 70, 140, 280, 560, 1120 },
                new[]{ ("memory_resonance", 1) }),

            // ── Tier 3: require deeper investment ────────────────────────────
            new UpgradeDef(
                "decay_mastery", "Decay Mastery",
                "Decay particle collection rate +25% per level.",
                UpgradeEffectType.DecayCollectionRate, 0.25f, 4,
                new[]{ 100, 250, 625, 1562 },
                new[]{ ("quantum_yield", 2) }),

            new UpgradeDef(
                "turnkey_builder", "Turnkey Builder",
                "Start each run with +1 pre-placed Harvester per level.",
                UpgradeEffectType.BuildingStartPrePlaced, 1f, 3,
                new[]{ 150, 750, 3750 },
                new[]{ ("efficient_layouts", 2) }),

            new UpgradeDef(
                "research_overdrive", "Research Overdrive",
                "Research timers complete 5% faster per level (max -50%).",
                UpgradeEffectType.ResearchSpeed, 0.05f, 10,
                new[]{ 30, 50, 85, 140, 230, 380, 625, 1030, 1700, 2800 },
                new[]{ ("memory_resonance", 2) }),

            new UpgradeDef(
                "entropy_echo", "Entropy Echo",
                "Earn +5% more prestige currency per run per level.",
                UpgradeEffectType.PrestigeGainMultiplier, 0.05f, 4,
                new[]{ 200, 600, 1800, 5400 },
                new[]{ ("entropy_headstart", 3), ("memory_resonance", 3) }),

            // ── Idle collection ───────────────────────────────────────────────
            new UpgradeDef(
                "idle_time_cap", "Dormant Resonance",
                "Idle harvesters keep running for +30 min longer per level (base 2 h, max 12 h).",
                UpgradeEffectType.IdleTimeCap, 1800f, 20,
                new[]{ 35, 40, 46, 53, 61, 70, 81, 93, 107, 123,
                       142, 163, 188, 216, 249, 286, 329, 379, 436, 502 },
                new[]{ ("entropy_headstart", 1) }),

            new UpgradeDef(
                "idle_collection_rate", "Idle Efficiency",
                "Idle harvesters collect +5% more output per level (base 50%, max 100%).",
                UpgradeEffectType.IdleCollectionRate, 0.05f, 10,
                new[]{ 80, 120, 180, 270, 405, 608, 912, 1368, 2052, 3078 },
                new[]{ ("idle_time_cap", 1) }),
        };

        // ── State ─────────────────────────────────────────────────────────────

        private readonly Dictionary<string, int> _levels = new();

        // ── Public API ────────────────────────────────────────────────────────

        public int GetLevel(string id) => _levels.TryGetValue(id, out int lvl) ? lvl : 0;

        /// <summary>
        /// Returns the sum of effectPerLevel × purchasedLevels across all upgrades of the given type.
        /// </summary>
        public float GetEffect(UpgradeEffectType type)
        {
            float total = 0f;
            foreach (var def in All)
                if (def.effectType == type)
                    total += def.effectPerLevel * GetLevel(def.id);
            return total;
        }

        /// <summary>True if all prerequisites for <paramref name="def"/> are satisfied.</summary>
        public bool PrereqsMet(UpgradeDef def)
        {
            foreach (var (reqId, minLevel) in def.prereqs)
                if (GetLevel(reqId) < minLevel) return false;
            return true;
        }

        /// <summary>Cost of the next level, or -1 if already maxed.</summary>
        public int NextLevelCost(UpgradeDef def)
        {
            int lvl = GetLevel(def.id);
            if (lvl >= def.maxLevel) return -1;
            return def.levelCosts[lvl]; // index 0 = level 1
        }

        /// <summary>
        /// Records one level purchase. Does NOT deduct currency — the caller is responsible.
        /// Fires <see cref="OnUpgradePurchased"/>.
        /// </summary>
        public bool AddLevel(string id)
        {
            var def = FindDef(id);
            if (def == null) return false;
            int cur = GetLevel(id);
            if (cur >= def.maxLevel) return false;
            _levels[id] = cur + 1;
            OnUpgradePurchased?.Invoke();
            return true;
        }

        // ── Save / load ───────────────────────────────────────────────────────

        /// <summary>
        /// Populates level data from the save list (format: "id:level").
        /// </summary>
        public void LoadFromSave(List<string> permanentUpgrades)
        {
            _levels.Clear();
            if (permanentUpgrades == null) return;
            foreach (var entry in permanentUpgrades)
            {
                int colon = entry.LastIndexOf(':');
                if (colon < 1) continue;
                string upId  = entry.Substring(0, colon);
                string lvlStr = entry.Substring(colon + 1);
                if (int.TryParse(lvlStr, out int lvl) && lvl > 0)
                    _levels[upId] = lvl;
            }
        }

        /// <summary>
        /// Serialises purchased levels back into the save list.
        /// </summary>
        public void FlushToSave(List<string> permanentUpgrades)
        {
            permanentUpgrades.Clear();
            foreach (var kv in _levels)
                if (kv.Value > 0)
                    permanentUpgrades.Add($"{kv.Key}:{kv.Value}");
        }

        /// <summary>
        /// Computes aggregate multiplier fields and writes them into the save.
        /// Called after any purchase so ECSLoadBridge can apply them on the next load.
        /// </summary>
        public void FlushEffectsToSave(SaveData save)
        {
            save.prestigeSpeedMultiplier  = 1f + GetEffect(UpgradeEffectType.CraftSpeedMultiplier);
            save.prestigeOutputMultiplier = 1f + GetEffect(UpgradeEffectType.OutputQuantityMultiplier);
            save.prestigeCostReduction    = GetEffect(UpgradeEffectType.BuildingCostReduction);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        public static UpgradeDef FindDef(string id)
        {
            foreach (var def in All)
                if (def.id == id) return def;
            return null;
        }
    }
}
