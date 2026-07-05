using System;
using UnityEngine;

namespace MobileIdleBuilder
{
    [Serializable]
    public struct BuildingUpgradeLevel
    {
        public int level;
        public float outputRate;             // production rate multiplier; for Maxwell's Demon: throughput multiplier
        public float powerCostEV;
        public float outputEV;               // for generators
        public float influenceRadiusTiles;   // for generators
        public float linkRadiusTiles;        // for power sources: node-to-node connection range
        public int costBaseCurrency;
        public int costPrestigeCurrency;     // 0 if not required
    }

    [Serializable]
    public struct BuildingStorageUpgradeLevel
    {
        public int level;
        public int maxOutputItems;           // max items in output buffer at this upgrade level
        public int costBaseCurrency;
        public int costPrestigeCurrency;
    }

    [Serializable]
    public struct BuildingInputUpgradeLevel
    {
        public int level;
        public int maxInputItems;            // max total items in input buffer at this upgrade level
        public int costBaseCurrency;
        public int costPrestigeCurrency;
    }

    [CreateAssetMenu(fileName = "New Building", menuName = "MobileIdleBuilder/Building")]
    public class BuildingSO : ScriptableObject
    {
        [Header("Identity")]
        public string id;
        public string displayName;
        [TextArea(1, 3)]
        public string description;
        public Sprite icon;
        public GameObject prefab;

        [Header("ECS Reference")]
        public int buildingId;

        [Header("Classification")]
        public int tier;
        public BuildingCategory category;
        public bool isObsoleteable;
        [TextArea(1, 2)]
        public string obsoleteCondition;

        [Header("Power")]
        public bool requiresPower;
        public bool isPowerSource;
        public float basePowerCostEV;
        public bool powerCostIsDynamic;     // true = driven by active recipe
        public float baseOutputEV;          // for power source buildings only
        public float influenceRadiusTiles;
        public float linkRadiusTiles;

        [Header("Production")]
        public float baseOutputRate;        // items per second
        public RecipeSO[] supportedRecipes;
        public int inputSlotCount;
        public string[] inputSlotLabels;    // e.g. ["Protons", "Neutrons", "Electrons"]
        public bool isEntropySink;          // true = Maxwell's Demon; items in input buffer are consumed for entropy

        [Header("Placement")]
        public Vector2Int footprint = Vector2Int.one; // width x height in grid cells
        public BuildingStructureKind structureKind;   // bespoke procedural form (None = placeholder cube)
        public BuildingPort[] ports;                  // input/output port layout in local (unrotated) space
        public PlacementRule placementRule;
        public BuildingCategory[] compatibleAdjacentCategories;
        public FieldType[] compatibleFields;

        [Header("Economy")]
        public int entropyCost;             // entropy spent to place this building

        [Header("Buffers")]
        public int baseMaxOutputItems;      // max items in output buffer before production stalls (0 = no limit)
        public int baseMaxInputItemsPerSlot;// max items per input slot before upstream conveyor stops (0 = no limit)

        [Header("Upgrades")]
        public BuildingUpgradeLevel[] upgradeLevels;
        public BuildingStorageUpgradeLevel[] storageUpgradeLevels; // increases maxOutputItems
        public BuildingInputUpgradeLevel[] inputUpgradeLevels;     // increases input buffer capacity

        [Header("Special Upgrade")]
        public bool hasSpecialUpgrade;
        public SpecialUpgradeSO specialUpgrade;

        [Header("Unlock")]
        public ResearchSO requiredResearch;
        public bool availableFromStart;

        [Header("Tutorial")]
        [TextArea(1, 2)]
        public string tutorialNote;

        [Header("Decay Collection")]
        public bool collectsDecayParticles;
        public float decayCollectionRate;

        [Header("Codex")]
        [TextArea(2, 5)]
        public string codexEntry;

        // ── Upgrade helpers ───────────────────────────────────────────────

        /// <summary>Returns the next speed upgrade entry (level == currentLevel+1), or null if maxed or no upgrades.</summary>
        public BuildingUpgradeLevel? NextSpeedUpgrade(int currentLevel)
        {
            if (upgradeLevels == null) return null;
            int target = currentLevel + 1;
            foreach (var u in upgradeLevels)
                if (u.level == target) return u;
            return null;
        }

        /// <summary>Returns the next storage upgrade entry (level == currentLevel+1), or null if maxed or no upgrades.</summary>
        public BuildingStorageUpgradeLevel? NextStorageUpgrade(int currentLevel)
        {
            if (storageUpgradeLevels == null) return null;
            int target = currentLevel + 1;
            foreach (var u in storageUpgradeLevels)
                if (u.level == target) return u;
            return null;
        }

        /// <summary>Returns the next input upgrade entry (level == currentLevel+1), or null if maxed or no upgrades.</summary>
        public BuildingInputUpgradeLevel? NextInputUpgrade(int currentLevel)
        {
            if (inputUpgradeLevels == null) return null;
            int target = currentLevel + 1;
            foreach (var u in inputUpgradeLevels)
                if (u.level == target) return u;
            return null;
        }

        /// <summary>Returns the max speed level (1 + number of speed upgrades defined).</summary>
        public int MaxSpeedLevel() => 1 + (upgradeLevels?.Length ?? 0);

        /// <summary>Returns the max storage level (1 + number of storage upgrades defined).</summary>
        public int MaxStorageLevel() => 1 + (storageUpgradeLevels?.Length ?? 0);

        /// <summary>Returns the max input level (1 + number of input upgrades defined).</summary>
        public int MaxInputLevel() => 1 + (inputUpgradeLevels?.Length ?? 0);

        /// <summary>
        /// Returns the ProductionSpeed multiplier for <paramref name="level"/>.
        /// Level 1 = baseline (1f). Higher levels look up outputRate from upgradeLevels.
        /// </summary>
        public static float ProductionSpeedForLevel(BuildingSO so, int level)
        {
            if (so == null || level <= 1 || so.upgradeLevels == null) return 1f;
            foreach (var u in so.upgradeLevels)
                if (u.level == level) return u.outputRate > 0f ? u.outputRate : 1f;
            return 1f;
        }

        // ── Power helpers ─────────────────────────────────────────────────

        /// <summary>
        /// Returns the eV draw for a power consumer at <paramref name="level"/>.
        /// Level 1 = so.basePowerCostEV. Higher levels look up powerCostEV from upgradeLevels,
        /// falling back to the base when the upgrade entry leaves it at 0.
        /// </summary>
        public static float PowerDrawForLevel(BuildingSO so, int level)
        {
            if (so == null) return 0f;
            if (level <= 1 || so.upgradeLevels == null) return so.basePowerCostEV;
            foreach (var u in so.upgradeLevels)
                if (u.level == level) return u.powerCostEV > 0f ? u.powerCostEV : so.basePowerCostEV;
            return so.basePowerCostEV;
        }

        /// <summary>
        /// Returns the eV output for a power source at <paramref name="level"/>.
        /// Level 1 = so.baseOutputEV. Higher levels look up outputEV from upgradeLevels.
        /// </summary>
        public static float PowerOutputForLevel(BuildingSO so, int level)
        {
            if (so == null) return 0f;
            if (level <= 1 || so.upgradeLevels == null) return so.baseOutputEV;
            foreach (var u in so.upgradeLevels)
                if (u.level == level) return u.outputEV > 0f ? u.outputEV : so.baseOutputEV;
            return so.baseOutputEV;
        }

        /// <summary>
        /// Returns the influence radius (tiles) for a power source at <paramref name="level"/>.
        /// Level 1 = so.influenceRadiusTiles. Higher levels look up influenceRadiusTiles from upgradeLevels.
        /// </summary>
        public static float InfluenceRadiusForLevel(BuildingSO so, int level)
        {
            if (so == null) return 0f;
            if (level <= 1 || so.upgradeLevels == null) return so.influenceRadiusTiles;
            foreach (var u in so.upgradeLevels)
                if (u.level == level) return u.influenceRadiusTiles > 0f ? u.influenceRadiusTiles : so.influenceRadiusTiles;
            return so.influenceRadiusTiles;
        }

        /// <summary>
        /// Returns the link (connection) radius (tiles) for a power source at <paramref name="level"/>.
        /// This is the node-to-node grid-connection range: two power buildings are linked when their
        /// footprint distance is within max(rangeA, rangeB). Level 1 = so.linkRadiusTiles; higher levels
        /// look up linkRadiusTiles from upgradeLevels, falling back to the base when the entry leaves it 0.
        /// </summary>
        public static float LinkRadiusForLevel(BuildingSO so, int level)
        {
            if (so == null) return 0f;
            if (level <= 1 || so.upgradeLevels == null) return so.linkRadiusTiles;
            foreach (var u in so.upgradeLevels)
                if (u.level == level) return u.linkRadiusTiles > 0f ? u.linkRadiusTiles : so.linkRadiusTiles;
            return so.linkRadiusTiles;
        }

        /// <summary>
        /// Returns the output buffer capacity for <paramref name="level"/>.
        /// Level 1 = so.baseMaxOutputItems (or 20 if unset). Higher levels use storageUpgradeLevels.
        /// </summary>
        public static int OutputCapacityForLevel(BuildingSO so, int level)
        {
            int baseline = (so != null && so.baseMaxOutputItems > 0) ? so.baseMaxOutputItems : 20;
            if (so == null || level <= 1 || so.storageUpgradeLevels == null) return baseline;
            foreach (var u in so.storageUpgradeLevels)
                if (u.level == level) return u.maxOutputItems > 0 ? u.maxOutputItems : baseline;
            return baseline;
        }

        /// <summary>
        /// Returns the input buffer capacity for <paramref name="level"/>.
        /// Level 1 = so.baseMaxInputItemsPerSlot (or 20 if unset). Higher levels use inputUpgradeLevels.
        /// </summary>
        public static int InputCapacityForLevel(BuildingSO so, int level)
        {
            int baseline = (so != null && so.baseMaxInputItemsPerSlot > 0) ? so.baseMaxInputItemsPerSlot : 20;
            if (so == null || level <= 1 || so.inputUpgradeLevels == null) return baseline;
            foreach (var u in so.inputUpgradeLevels)
                if (u.level == level) return u.maxInputItems > 0 ? u.maxInputItems : baseline;
            return baseline;
        }
    }
}
