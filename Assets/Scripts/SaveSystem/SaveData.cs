using System;
using System.Collections.Generic;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Full save data structure — serialized to JSON for both local file and cloud storage.
    /// Persistent fields survive prestige; currentRun is cleared on prestige.
    /// </summary>
    [Serializable]
    public class SaveData
    {
        public string playerId;
        public string lastSaved;           // ISO 8601
        public int   prestigeCount;
        public long  prestigeCurrency;
        public float prestigeSpeedMultiplier  = 1f;
        public float prestigeOutputMultiplier = 1f;
        public float prestigeCostReduction    = 0f;
        public List<string> permanentUpgrades = new();
        public List<string> unlockedRecipes   = new();
        public List<string> unlockedResearch  = new();
        public List<string> codex                           = new();
        public List<string> achievements                    = new();
        public List<AchievementProgressEntry> achievementProgress = new();
        public TutorialSaveData tutorial                     = new();
        public PVPRunData pvpRun                            = new();
        public CurrentRunData currentRun                    = new();
    }

    [Serializable]
    public class CurrentRunData
    {
        public long  baseCurrency;
        public long  totalEntropySpent;
        public float baseNetWorth;
        public List<string> inventoryKeys           = new();
        public List<int>    inventoryValues         = new();
        public List<string> nonPersistentUpgrades   = new();
        public GridSaveData grid                    = new();
        public List<string> researchProgressKeys    = new();
        public List<float>  researchProgressValues  = new();
    }

    [Serializable]
    public class GridSaveData
    {
        public GridSize size              = new();
        public List<GridExpansion> expansions    = new();
        public List<BuildingSaveData> buildings  = new();
        public List<ConveyorSaveData> conveyors  = new();
        public List<FieldSaveData>   fields      = new();
    }

    [Serializable]
    public class FieldSaveData
    {
        public string fieldId;  // matches FieldSO.id
        public int[]  position; // [x, y] grid cell
    }

    [Serializable]
    public class GridSize
    {
        public int x;
        public int y;
    }

    [Serializable]
    public class GridExpansion
    {
        public int x;
        public int y;
        public int size;
    }

    [Serializable]
    public class BuildingSaveData
    {
        public int   buildingId;      // matches BuildingSO.buildingId
        public int   recipeId;        // matches RecipeSO.recipeId; -1 = none
        public int[] position;        // [x, y] anchor cell
        public int   level;
        public int   rotation;        // 0-3 CW
        public bool  flipped;
        public int   outputDirection; // -1 = not a field-collector
    }

    [Serializable]
    public class ConveyorSaveData
    {
        public int[] cells; // flattened [x0,y0, x1,y1, ...] ordered head→tail
    }

    /// <summary>
    /// Tutorial progress persisted across sessions.
    /// hasCompletedFirstRun survives prestige — once true the tutorial is never shown again.
    /// </summary>
    [Serializable]
    public class TutorialSaveData
    {
        /// <summary>TutorialStepDef.id of the active step; "" = start from beginning.</summary>
        public string currentStepId       = "";
        public bool   isActive            = true;
        public bool   hasCompletedFirstRun = false;
    }

    /// <summary>Tracks in-progress count for achievements that require N of something.</summary>
    [Serializable]
    public class AchievementProgressEntry
    {
        public string id;
        public int count;
    }

    /// <summary>
    /// Persistent PVP competition state for the current player.
    /// state: "Available" | "InRun" | "Completed"
    /// Locked is derived at runtime from prestigeCount.
    /// </summary>
    [Serializable]
    public class PVPRunData
    {
        public string state        = "Available"; // Available | InRun | Completed
        public string runStartUtc;                // ISO 8601
        public string runEndUtc;                  // ISO 8601  (start + 48 h)
        public long   submittedScore;             // set after server submission
    }
}
