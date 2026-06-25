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
        public long  prestigeCurrencySpent;
        public float prestigeSpeedMultiplier  = 1f;
        public float prestigeOutputMultiplier = 1f;
        public float prestigeCostReduction    = 0f;
        public List<string> permanentUpgrades = new();
        public List<string> unlockedSites      = new();   // site ids unlocked; survives prestige. site_origin implicit.
        public bool  domainsIntroSeen;                     // one-shot: Quantum Domains intro dialogue shown. Survives prestige.
        public IdleCollectionSnapshot idleSnapshot = new();   // active site's offline chain snapshot (mirrors siteSnapshots[activeSiteIndex])
        public List<IdleCollectionSnapshot> siteSnapshots = new(); // per-site offline snapshots; index = site index. Inactive sites keep producing from these. Cleared on prestige.
        public string idleCollectionApplied;   // ISO 8601 — set after each session's offline calc to prevent double-apply
        public List<string> unlockedRecipes   = new();
        public List<string> unlockedResearch  = new();
        public string activeResearchId;                     // id of the research currently in progress; null/empty = none (one at a time). Cleared on prestige.
        public string activeResearchCompleteUtc;            // ISO 8601 UTC — when the active research finishes (same pattern as speedBoostExpiryUtc). Cleared on prestige.
        public List<string> codex                           = new();
        public long  paidCurrency;                          // Crystals (◆) — earned via achievements / IAP
        public long  crystalsPurchased;                     // Lifetime IAP crystals (audit trail)
        public float lastKnownNetWorth;                     // Snapshot written by ECSLoadBridge; used by shop entropy scaling
        public string speedBoostExpiryUtc;                  // ISO 8601 — null/empty means no active speed boost
        public List<string> achievements                    = new();
        public List<AchievementProgressEntry> achievementProgress = new();
        public List<string> unclaimedAchievements           = new(); // completed but reward not yet claimed
        public string dailyResetUtc;   // ISO 8601 — when current daily period expires
        public string weeklyResetUtc;  // ISO 8601 — when current weekly period expires (Mon 00:00 UTC)
        public string monthlyResetUtc; // ISO 8601 — when current monthly period expires (1st of month)
        // Daily events (login streak + rotating challenges) — survive prestige, NOT in PrestigeSystem reset.
        public int    loginStreakIndex;          // 0-based position in the 28-day login reward calendar
        public string lastLoginRewardUtc;        // ISO 8601 — date of the last claimed login reward
        public string dailyChallengeResetUtc;    // ISO 8601 — when the current challenge set expires
        public List<string> dailyChallengeIds                       = new(); // today's 3 challenge ids
        public List<AchievementProgressEntry> dailyChallengeProgress = new(); // per-challenge accumulated progress
        public List<string> dailyChallengesClaimed                  = new(); // challenge ids already claimed today
        // Rewarded ads — opt-in video rewards with per-placement daily caps. Survive prestige
        // (not cleared by PrestigeSystem), mirroring the daily-event fields above.
        public List<AchievementProgressEntry> adWatchCounts         = new(); // id = AdPlacement name -> count watched today
        public string adWatchResetUtc;                                       // ISO 8601 — when adWatchCounts were last zeroed (00:00 UTC)
        public string adsIdleBoostExpiryUtc;                                 // ISO 8601 — null/empty = no active +50% idle-rate boost
        // Managers (hireable building crew) — survive prestige; NOT cleared in PrestigeSystem.
        // hiredManagers: ManagerSO.id of every hired manager. managerAssignments: which hired manager
        // is bound to which building. Buildings are destroyed on prestige, so assignments are cleared
        // there (the hired list persists).
        public List<string> hiredManagers                           = new(); // ManagerSO.id list
        public List<ManagerAssignmentEntry> managerAssignments      = new(); // manager -> building binding
        public List<ManagerStarEntry> managerStars                  = new(); // per-manager star tier (missing -> 1)
        // Megastructure (Dyson Sphere endgame project) — a meta-progression layer ABOVE prestige.
        // Late-game components take days to craft, so completed stages AND partial contributions both
        // survive prestige (these fields live here, not in currentRun, and PrestigeSystem never clears them).
        public int          megastructureStage        = 0;     // completed stage count (0-5)
        public List<string> megastructureContribKeys   = new(); // item ids (string numeric, like inventoryKeys), toward the CURRENT stage
        public List<int>    megastructureContribValues = new(); // contributed counts, parallel to megastructureContribKeys
        public TutorialSaveData tutorial                     = new();
        public PVPRunData pvpRun                            = new();
        public CurrentRunData currentRun                    = new();
    }

    [Serializable]
    public class IdleCollectionSnapshot
    {
        public List<IdleChainEntry> chains      = new();
        public string snapshotTimestampUtc;
    }

    [Serializable]
    public class IdleChainEntry
    {
        public int   itemId;
        public float itemsPerSecond;
        public bool  endsAtEntropySink;
        public float baseSellValue;
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
        public GridSaveData grid                    = new();   // legacy single-grid mirror of grids[0]; kept for backward-compat reads
        public List<GridSaveData> grids             = new();   // index = site index; grids[0] mirrors 'grid'. Reset on prestige.
        public int          activeSiteIndex         = 0;       // index into grids of the live site
        public List<string> researchProgressKeys    = new();
        public List<float>  researchProgressValues  = new();

        /// <summary>
        /// The grid for the currently active site. Falls back to the legacy single 'grid'
        /// when 'grids' has not yet been populated (pre-multi-grid saves). All grid access
        /// should go through this property so migration stays centralized.
        /// (JsonUtility serializes fields only, so this property is not persisted.)
        /// </summary>
        public GridSaveData ActiveGrid
        {
            get
            {
                if (grids != null && grids.Count > 0)
                {
                    int idx = (activeSiteIndex >= 0 && activeSiteIndex < grids.Count) ? activeSiteIndex : 0;
                    return grids[idx];
                }
                return grid;
            }
        }
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

        // Tap cooldown (radial wheel). Empty/null = ready. ISO 8601 UTC instant the field recharges,
        // so the cooldown keeps ticking while the app is closed and cannot be reset by relaunching.
        public string cooldownEndUtc;
        public float  cooldownDurationSec; // full length when started, so the wheel resumes its fraction
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
        public int   storageLevel;
        public int   inputLevel;
        public int   rotation;        // 0-3 CW
        public bool  flipped;
        public int   outputDirection; // -1 = not a field-collector
    }

    [Serializable]
    public class ConveyorSaveData
    {
        public int[] cells;        // flattened [x0,y0, x1,y1, ...] ordered head→tail
        public int   singleDir = -1; // facing for a 1-cell chain (OutputDirection); -1 = derive from cells
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
    /// Binds one hired manager to one placed building. Survives prestige only as far as the manager
    /// (the building is destroyed, so these entries are cleared on prestige).
    /// <para>
    /// Buildings have no stable id across save/load (BuildingSaveData is positional), so the building
    /// is identified by (<see cref="siteIndex"/>, grid anchor position). The anchor is encoded as
    /// <c>x * 10000 + y</c>. Anchor positions are stable across save/restore because LoadGrid re-places
    /// buildings at their saved positions. The site index is required because, with multi-grids, two
    /// sites can hold a building at the same anchor; only the active site has live ECS entities, so a
    /// manager bonus is (re)applied to a building only when its siteIndex matches the active site.
    /// </para>
    /// </summary>
    [Serializable]
    public class ManagerAssignmentEntry
    {
        public string managerId;      // ManagerSO.id of the assigned manager
        public int    siteIndex;      // index into currentRun.grids of the building's site
        public int    buildingPosKey; // anchor position encoded as x * 10000 + y
    }

    /// <summary>
    /// Per-manager star tier. Stars survive prestige (like the hired roster) and scale the manager's
    /// effective bonus. A hired manager with no entry here is treated as 1 star (the shipped base
    /// value), so legacy saves load without migration.
    /// </summary>
    [Serializable]
    public class ManagerStarEntry
    {
        public string managerId; // ManagerSO.id
        public int    stars;     // 1-based star tier (1 = base)
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
