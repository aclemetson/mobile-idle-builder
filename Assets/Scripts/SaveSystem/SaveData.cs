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
        public int prestigeCount;
        public long prestigeCurrency;
        public List<string> permanentUpgrades = new();
        public List<string> unlockedRecipes   = new();
        public List<string> unlockedResearch  = new();
        public List<string> codex                           = new();
        public List<string> achievements                    = new();
        public List<AchievementProgressEntry> achievementProgress = new();
        public PVPRunData pvpRun                            = new();
        public CurrentRunData currentRun                    = new();
    }

    [Serializable]
    public class CurrentRunData
    {
        public long baseCurrency;
        public Dictionary<string, int> inventory         = new();
        public List<string> nonPersistentUpgrades        = new();
        public GridSaveData grid                         = new();
        public Dictionary<string, float> researchProgress = new();
    }

    [Serializable]
    public class GridSaveData
    {
        public GridSize size              = new();
        public List<GridExpansion> expansions = new();
        public List<BuildingSaveData> buildings = new();
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
        public string type;
        public int[] position;   // [x, y]
        public int level;
        public int[] from;       // conveyor only
        public int[] to;         // conveyor only
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
