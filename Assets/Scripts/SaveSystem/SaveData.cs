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
        public List<string> unlockedWorlds     = new();   // world ids unlocked; survives prestige. world_physics implicit (like site_origin).
        public List<string> worldsIntroSeen    = new();   // one-shot per world: world-unlock intro dialogue shown. Survives prestige.
        public List<WorldTutorialSaveData> worldTutorials = new(); // per-world tutorial progress (Chemistry...); Physics uses `tutorial`. Survives prestige.
        public string lastSeenWhatsNewHash;                // WhatsNew.Hash of the release notes last shown (#502). Survives prestige.
        public string lastSeenGameDataUtc;                 // ISO 8601 UTC — newest gamedata.updatedUtc the player has acknowledged via the update-notice modal. Survives prestige.
        public bool  devConsoleUsed;                       // sticky: a dev-console command ran on this save (alpha builds ship the console). Stamped on telemetry snapshots so balancing data can exclude it. Survives prestige.
        public List<string> platforms         = new();     // every platform that ever wrote this save (SavePlatform: Editor, Android, iOS, ...). Sticky; read by scripts/purge_test_players.py so a save that was ever on a phone is never purged. Survives prestige.
        public long  playtimeSec;                          // lifetime foreground play time in seconds (TelemetryService accumulates it, analytics on or off). Reported as tutorial_step.playtime_total_sec. Survives prestige. Starts at 0 for saves from before it existed.
        // Public player name (PvE phase 1, PlayerNameService). UGS Authentication holds the real one; this caches it
        // for offline display. Full UGS form "Name#1234"; "" = never chosen. Survives prestige.
        public string playerName                    = "";
        // Tournament item stars (PvE phase 2, ItemStars): +20% value per star, permanent. Parallel lists keyed by item
        // STRING id (stable across renumbering). Survive prestige and Ascension.
        public List<string> itemStarIds             = new();
        public List<int>    itemStarCounts          = new();
        public bool   namePromptSkipped;                   // the post-tutorial "Choose your name" card was skipped; only Settings (and PvE) ask again. Survives prestige.
        public IdleCollectionSnapshot idleSnapshot = new();   // active site's offline chain snapshot (mirrors siteSnapshots[activeSiteIndex])
        public List<IdleCollectionSnapshot> siteSnapshots = new(); // per-site offline snapshots; index = site index. Inactive sites keep producing from these. Cleared on prestige.
        public string idleCollectionApplied;   // ISO 8601 — set after each session's offline calc to prevent double-apply
        // Recipe knowledge (recipes researched in any run), mirrored from recipe_knowledge.json so it roams
        // with the cloud save. Survives prestige. recipeKnowledgeVersion 0 = never written (older save):
        // RecipeKnowledgeService back-fills it from the device file; > 0 = authoritative for this profile.
        public List<string> unlockedRecipes   = new();
        public int recipeKnowledgeVersion;
        public List<string> unlockedResearch  = new();
        public string activeResearchId;                     // id of the research currently in progress; null/empty = none (one at a time). Cleared on prestige.
        public string activeResearchCompleteUtc;            // ISO 8601 UTC — when the active research finishes (same pattern as speedBoostExpiryUtc). Cleared on prestige.
        public List<string> codex                           = new();
        public bool  adFree;                                // Ad-free purchase owned (#503): ad rewards without ads + 20% income. Survives prestige.
        public long  pveStrings;
        public List<string> pveTrack = new();
        public List<string> pveCosmetics = new();           // owned cosmetic ids (PveCosmetics, #500)
        public string pveNameColor;                         // equipped leaderboard name colour ("" / null = none)
        public string pveTitle;                             // equipped leaderboard title
        public string pveBeltTheme;                         // equipped belt theme (own grid only)               // tournament track levels "id:level" (TournamentTrack, #500). Survives prestige.                            // Strings, the tournament currency (#500). Survives prestige and Ascension.
        public long  paidCurrency;                          // Crystals (◆) — earned via achievements / IAP
        public long  crystalsPurchased;                     // Lifetime IAP crystals (audit trail)
        public double lastKnownNetWorth;                    // Snapshot written by ECSLoadBridge; used by shop entropy scaling
        public string speedBoostExpiryUtc;                  // ISO 8601 — null/empty means no active speed boost
        public List<string> achievements                    = new();
        public List<AchievementProgressEntry> achievementProgress = new();
        public List<string> unclaimedAchievements           = new(); // completed but reward not yet claimed
        public string dailyResetUtc;   // ISO 8601 — when current daily period expires
        public string weeklyResetUtc;  // ISO 8601 — when current weekly period expires (Mon 00:00 UTC)
        public string monthlyResetUtc; // ISO 8601 — when current monthly period expires (1st of month)
        // Daily events (login reward calendar) — survive prestige, NOT in PrestigeSystem reset.
        public int    loginStreakIndex;          // 0-based position in the 28-day login reward calendar
        public string lastLoginRewardUtc;        // ISO 8601 — date of the last claimed login reward
        // Rewarded ads — opt-in video rewards with per-placement daily caps. Survive prestige
        // (not cleared by PrestigeSystem), mirroring the daily-event fields above.
        public List<AchievementProgressEntry> adWatchCounts         = new(); // id = AdPlacement name -> count watched today
        public string adWatchResetUtc;                                       // ISO 8601 — when adWatchCounts were last zeroed (00:00 UTC)
        public List<AdLastWatchEntry> adLastWatch = new();                  // id = AdPlacement name -> last watch (ISO 8601 UTC); drives per-placement cooldowns (Entropy Surge). Survives prestige.
        public string adsIdleBoostExpiryUtc;                                 // ISO 8601 — null/empty = no active +50% idle-rate boost
        // Cyclotron Kick minigame (CyclotronProgress) — survives prestige. Unlocks at the 2nd prestige.
        public string cyclotronLastRunUtc;      // ISO 8601 — when the last run started; next run a cooldown (60 min, less with upgrades) later. null = never played (ready)
        public string cyclotronBoostExpiryUtc;  // ISO 8601 — when the current entropy boost ends; null/past = no boost
        public float  cyclotronBoostMultiplier; // x1.5..x3 — entropy multiplier of the current boost (only while unexpired)
        public int    cyclotronInsight;         // Insight points banked; spent on the two Cyclotron upgrades
        public int    cyclotronBoostLevel;      // Longer Boost level, 0..10 (+1 min boost each)
        public int    cyclotronCooldownLevel;   // Faster Recharge level, 0..6 (-5 min cooldown each)
        public bool   cyclotronIntroSeen;       // one-shot: intro_cyclotron dialogue played when it unlocked
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
        // World delivery tasks (Chemistry gate, 2026-11-19): items delivered to the Demon toward the CURRENT
        // stage. Survive prestige like the megastructure; WorldTasks reads and writes them.
        public int          worldTaskStage            = 0;     // completed stage count
        public List<string> worldTaskProgressKeys     = new(); // item ids (string numeric), toward the current stage
        public List<int>    worldTaskProgressValues   = new(); // delivered counts, parallel to worldTaskProgressKeys
        public int          worldTaskVersion          = 0;     // 0 = not yet migrated (start stage from prestigeCount)
        // Delivery tasks of the other worlds (Metallurgy, Engineering: 2026-11-23), one entry per world. The Chemistry
        // gate keeps the fields above. Survive prestige the same way.
        public List<WorldTaskProgressData> worldTaskSets = new();
        // Ascension (2026-11-23, AscensionRules): survive prestige and Ascension.
        public int          ascensionCount            = 0;
        public long         megastructureMaterial     = 0;
        public int          megastructureExpansion    = 0;     // Expansion levels bought with material
        public TutorialSaveData tutorial                     = new();
        public PVPRunData pvpRun                            = new();   // LEGACY: the old 48h PvP stub (removed 2026-11-23). Kept so old saves load; never read.
        public TournamentSaveData tournament                = new();   // PvE tournament this device entered (TournamentService). Survives prestige.
        // Weeks whose PvE reward this save has applied (crystals + stars). The reward is applied here first, then
        // cleared on the server; this list stops a retry of the server step from granting it twice. Newest last,
        // trimmed to TournamentRules.ClaimedWeeksKept. Survives prestige.
        public List<string> pveClaimedWeeks                 = new();
        // PvE onboarding (TournamentIntroController): the full intro has been shown once; tournamentIntroWeek = the
        // week id whose "tournament open" message was last shown (one per entry window). Survive prestige.
        public bool   tournamentIntroSeen;
        public string tournamentIntroWeek;
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
        // Entropy-only entry: byproduct escape entropy or a surplus sale. It pays entropy but no item reaches
        // the Demon, so it never counts toward world delivery tasks. Absent (false) in older saves.
        public bool  notDelivered;
    }

    [Serializable]
    public class CurrentRunData
    {
        public long  baseCurrency;
        public long  totalEntropySpent;
        public long  totalEntropyDiscounted;   // list price minus price paid; net worth counts purchases at list price
        public double baseNetWorth;
        public int    freeResearchUsed;            // research this run that skipped its timer (Free Research, #491)
        public List<string> inventoryKeys           = new();
        public List<int>    inventoryValues         = new();
        public List<string> nonPersistentUpgrades   = new();
        public GridSaveData grid                    = new();   // legacy single-grid mirror of grids[0]; kept for backward-compat reads
        public List<GridSaveData> grids             = new();   // index = site index; grids[0] mirrors 'grid'. Reset on prestige.
        public int          activeSiteIndex         = 0;       // index into grids of the live site
        public int          activeWorldIndex        = 0;       // index into WorldDatabase of the active world; 0 = Physics. Keep consistent with activeSiteIndex via WorldLayout.
        public List<string> researchProgressKeys    = new();
        public List<float>  researchProgressValues  = new();
        // Fields bought this run (FieldPurchaseService), parallel lists: FieldSO.id -> purchases.
        // A quark pair counts once. Each purchase raises the next price; cleared with the run on prestige.
        public List<string> fieldPurchaseIds        = new();
        public List<int>    fieldPurchaseCounts     = new();
        // Recipe-menu craft queue (ManualCraftService, #310). Inputs were taken when each item was
        // queued; cleared with the run on prestige.
        public CraftQueueSaveData craftQueue        = new();

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

    /// <summary>One queued recipe-menu craft: <c>count</c> items of a recipe, each taking <c>unitSeconds</c>.</summary>
    [Serializable]
    public class CraftQueueEntry
    {
        public string recipeId;
        public int    count;
        public float  unitSeconds;
    }

    /// <summary>
    /// The recipe-menu craft queue. Only the front entry is in progress; it finishes at
    /// <c>activeCompleteUtc</c> (ISO 8601 "O", same timed-effect pattern as research). Logic: <see cref="CraftQueue"/>.
    /// </summary>
    [Serializable]
    public class CraftQueueSaveData
    {
        public List<CraftQueueEntry> entries = new();
        public string activeCompleteUtc      = "";
    }

    [Serializable]
    public class GridSaveData
    {
        public GridSize size              = new();
        public List<GridExpansion> expansions    = new();
        public List<BuildingSaveData> buildings  = new();
        public List<ConveyorSaveData> conveyors  = new();
        public List<FieldSaveData>   fields      = new();
        public int[] blockedOutputs;             // flattened [x0,y0,...] of belt cells whose forward output
                                                 // is an intentional dead-end (ran up to a belt without merging)
        public bool  startersPlaced;             // the site's starting buildings (SiteSO.startingBuildings) were placed this run
        public int[] splits;                     // belt splits, flattened [x, y, mask, w0, w1, w2, w3, ...]
                                                 // (BeltSplitRules.Stride per split); null = none
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
        public int[] portFilters;     // Filter rules [facing, itemId, …] (PortFilterRules); null = none / defaults
        public int   stackSize;       // stream stack setting (StreamStack, Chemistry producers) 1-5; 0 = 1 (older saves)
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

    /// <summary>Delivery-task progress toward unlocking one world (all but the Chemistry gate). Survives prestige.</summary>
    [Serializable]
    public class WorldTaskProgressData
    {
        public string       worldId = "";
        public int          stage;                 // completed stage count
        public List<string> keys   = new();        // item ids (string numeric), toward the current stage
        public List<int>    values = new();        // delivered counts, parallel to keys
    }

    /// <summary>Progress through one world's own tutorial (WorldSO.tutorialFlow). Survives prestige.</summary>
    [Serializable]
    public class WorldTutorialSaveData
    {
        public string worldId       = "";
        /// <summary>TutorialStepDef.id of the step to resume at; "" = from the start.</summary>
        public string currentStepId = "";
        public bool   done;
    }

    /// <summary>Tracks in-progress count for achievements that require N of something.</summary>
    [Serializable]
    public class AchievementProgressEntry
    {
        public string id;
        public int count;
    }

    /// <summary>When a rewarded-ad placement was last watched (starts its cooldown).</summary>
    [Serializable]
    public class AdLastWatchEntry
    {
        public string id;   // AdPlacement name
        public string utc;  // ISO 8601 UTC
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
    /// The PvE tournament the player entered (PvE phase 4, <c>TournamentService</c>). The server holds the truth
    /// (docs/runbooks/pve-backend-setup.md); this copy drives the run timer, the prestige lock and when snapshots
    /// are sent while offline from the server. Root field: survives prestige (the tournament run IS the run that
    /// starts with the join's prestige).
    /// </summary>
    [Serializable]
    public class TournamentSaveData
    {
        public string week;                 // week id (Friday date) of the tournament entered; null = never
        public string bracketId;
        public string league;
        public string startUtc;             // server time the 24h run started (ISO 8601)
        public string runEndsUtc;           // start + 24h
        /// <summary>
        /// prestigeCount when the player joined; the join's prestige is still pending while
        /// <c>SaveData.prestigeCount</c> has not passed it. No snapshot is sent before then: the old run's
        /// net worth would make every later (post-reset) snapshot look like a drop. -1 = not pending.
        /// </summary>
        public int    prestigeCountAtJoin = -1;
        public bool   finalSent;            // the snapshot at the 24h mark went through
        public bool   retired;              // left the run early (#505): score frozen on the server, run over for this device
        // The latest snapshot taken during the run that the server hasn't confirmed (pendingT = its elapsed
        // seconds; -1 = none). Kept so a snapshot lost when the OS suspended the app still counts: it is sent
        // on the next launch with its own time (TournamentRules.CapturePending, PveRules.SnapshotTime).
        public int    pendingT = -1;
        public double pendingNw;
        public double pendingRate;
        public int    pendingCap;
    }

    /// <summary>
    /// LEGACY: state of the removed 48h PvP stub. Never read; kept so saves that have it still load.
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
