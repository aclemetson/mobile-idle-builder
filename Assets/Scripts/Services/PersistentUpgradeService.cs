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
        /// <summary>For WorldCraftSpeed / WorldOutput: the world the bonus applies in (WorldSO id); null = every world.</summary>
        public readonly string              world;

        public UpgradeDef(string id, string displayName, string description,
            UpgradeEffectType effectType, float effectPerLevel, int maxLevel,
            int[] levelCosts, (string, int)[] prereqs = null, string world = null)
        {
            this.world          = world;
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

            new UpgradeDef(
                "field_cooldown", "Quick Hands",
                "Field tap cooldown is 5% shorter per level (max -50%).",
                UpgradeEffectType.FieldCooldownReduction, 0.05f, 10,
                new[]{ 20, 35, 55, 85, 130, 200, 305, 465, 705, 1070 }),

            // ── Tier 2: require 1 Tier-1 level ───────────────────────────────
            // 10 levels since the Item Timeline (2026-11-23): its walls (Sodium, Copper) expect the later ones.
            new UpgradeDef(
                "quantum_yield", "Quantum Yield",
                "All recipes produce +10% more output per level.",
                UpgradeEffectType.OutputQuantityMultiplier, 0.10f, 10,
                new[]{ 40, 80, 160, 320, 640, 960, 1500, 2200, 3300, 4900 },
                new[]{ ("assembly_line", 1) }),

            new UpgradeDef(
                "efficient_layouts", "Efficient Layouts",
                "Building placement costs 5% less per level (max −30%).",
                UpgradeEffectType.BuildingCostReduction, 0.05f, 6,
                new[]{ 35, 70, 140, 280, 560, 1120 },
                new[]{ ("memory_resonance", 1) }),

            new UpgradeDef(
                "salvage_protocols", "Salvage Protocols",
                "Removing a building refunds +10% more of the entropy spent on it per level (base 50%, max 100%).",
                UpgradeEffectType.DemolishRefund, 0.10f, 5,
                new[]{ 30, 60, 120, 240, 480 },
                new[]{ ("efficient_layouts", 1) }),

            new UpgradeDef(
                "surplus_resale", "Surplus Brokerage",
                "Items a building can't use are sold automatically for +10% more of the Demon price per level (base 50%, max 100%).",
                UpgradeEffectType.SurplusResale, 0.10f, 5,
                new[]{ 20, 40, 80, 160, 320 }),

            // ── Tier 3: require deeper investment ────────────────────────────
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

            // ── World tracks (Item Timeline, 2026-11-23) ─────────────────────
            // Each world has its own track, bought at its walls: the research there costs more than one run
            // earns, so the player prestiges at the same spot and buys a level or two each time. A level costs
            // about half a run's prestige currency at its wall (docs/item-timeline.html, Prestige upgrades).
            new UpgradeDef(
                "fission_yield", "Fission Yield",
                "Physics buildings produce +15% more output per level.",
                UpgradeEffectType.WorldOutput, 0.15f, 6,
                new[]{ 95, 140, 190, 270, 370, 520 }, null, WorldGate.Physics),
            new UpgradeDef(
                "neutron_economy", "Neutron Economy",
                "Physics buildings craft 10% faster per level.",
                UpgradeEffectType.WorldCraftSpeed, 0.10f, 5,
                new[]{ 130, 180, 250, 340, 480 },
                new[]{ ("fission_yield", 1) }, WorldGate.Physics),
            new UpgradeDef(
                "spring_pressure", "Spring Pressure",
                "Chemistry Springs pour +20% more water per level, so more buildings run at full speed.",
                UpgradeEffectType.WorldStreamFlow, 0.20f, 6,
                new[]{ 89, 130, 180, 250, 340, 480 }, null, WorldGate.Chemistry),
            new UpgradeDef(
                "catalysis", "Catalysis",
                "Chemistry buildings react 15% faster per level.",
                UpgradeEffectType.WorldCraftSpeed, 0.15f, 8,
                new[]{ 120, 170, 230, 320, 450, 630, 870, 1300 },
                new[]{ ("spring_pressure", 1) }, WorldGate.Chemistry),
            new UpgradeDef(
                "growth_media", "Growth Media",
                "Biology buildings produce +15% more output per level.",
                UpgradeEffectType.WorldOutput, 0.15f, 8,
                new[]{ 180, 250, 350, 480, 680, 940, 1400, 1900 }, null, WorldGate.Biology),
            new UpgradeDef(
                "furnace_heat", "Furnace Heat",
                "Metallurgy buildings smelt 15% faster per level.",
                UpgradeEffectType.WorldCraftSpeed, 0.15f, 8,
                new[]{ 220, 310, 440, 610, 850, 1200, 1700, 2400 }, null, WorldGate.Metallurgy),
            // ── Idle collection ───────────────────────────────────────────────
            // A short 30 min base cap brings players back to collect; the first levels are cheap and have
            // no prerequisite so the first prestige (~50✦) buys the first four levels (2.5 h) straight away.
            // 23 levels take the cap from 30 min to the 12 h absolute max.
            new UpgradeDef(
                "idle_time_cap", "Dormant Resonance",
                "Idle harvesters keep running for +30 min longer per level (base 30 min, max 12 h).",
                UpgradeEffectType.IdleTimeCap, 1800f, 23,
                new[]{ 5, 10, 15, 20, 25, 30, 40, 50, 60, 75,
                       90, 110, 130, 155, 185, 220, 260, 305, 360, 425,
                       500, 590, 700 }),

            new UpgradeDef(
                "idle_collection_rate", "Idle Efficiency",
                "Idle harvesters collect +5% more output per level (base 20%, max 100%).",
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

        /// <summary>
        /// Sum of the world-only upgrades of <paramref name="type"/> (WorldCraftSpeed / WorldOutput) that apply in
        /// <paramref name="worldId"/> (null = Physics). An upgrade with no world applies everywhere.
        /// </summary>
        public float GetWorldEffect(UpgradeEffectType type, string worldId)
        {
            worldId = string.IsNullOrEmpty(worldId) ? WorldGate.Physics : worldId;
            float total = 0f;
            foreach (var def in All)
                if (def.effectType == type && (def.world == null || def.world == worldId))
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
            if (def.effectType == UpgradeEffectType.WorldCraftSpeed || def.effectType == UpgradeEffectType.WorldOutput
                || def.effectType == UpgradeEffectType.WorldStreamFlow)
                WorldRules.RefreshWorldBonus();
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
