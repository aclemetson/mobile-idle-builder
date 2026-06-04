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
    }
}
