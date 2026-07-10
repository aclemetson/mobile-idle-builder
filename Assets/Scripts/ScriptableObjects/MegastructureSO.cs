using System;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// The kind of permanent, stacking bonus a completed megastructure stage grants.
    /// Enum names are referenced by string in game_data.json (parsed by the importer), so do not rename
    /// without updating the JSON.
    /// </summary>
    public enum MegastructureRewardType
    {
        OutputMultiplier,       // adds rewardValue to the global output multiplier (0.10 = +10%)
        SpeedMultiplier,        // adds rewardValue to the global craft-speed multiplier
        PrestigeGainMultiplier  // adds rewardValue to the prestige-currency gain bonus (1.0 = +100% => x2)
    }

    /// <summary>
    /// One stage of the megastructure construction project. The player contributes the full
    /// <see cref="costs"/> bill of materials; on completion the stage grants <see cref="rewardType"/> /
    /// <see cref="rewardValue"/> permanently.
    /// </summary>
    [Serializable]
    public class MegastructureStage
    {
        public string id;
        public string displayName;
        public ItemSO[] costItems;          // parallel to costQuantities
        public int[]    costQuantities;     // parallel to costItems
        public MegastructureRewardType rewardType;
        public float    rewardValue;
    }

    /// <summary>
    /// Single endgame construction project (Dyson Sphere). A meta-progression layer above prestige:
    /// completed stages and partial contributions both survive prestige (state lives in the persistent
    /// section of SaveData). Generated from game_data.json by GameDataImporter as a single asset.
    /// </summary>
    [CreateAssetMenu(fileName = "Megastructure", menuName = "MobileIdleBuilder/Megastructure")]
    public class MegastructureSO : ScriptableObject
    {
        [Header("Identity")]
        public string id;
        public string displayName;

        [Header("Gating")]
        [Tooltip("ResearchSO.id that must be unlocked before the megastructure panel/button appears.")]
        public string requiredResearch;

        [Header("Stages")]
        public MegastructureStage[] stages = Array.Empty<MegastructureStage>();
    }
}
