using System;
using System.Collections.Generic;

// Shared by the Unity client and the PvE Cloud Code module (CloudCode/PveModule links this file).
// Plain C# 9 only: no UnityEngine. Public fields (not properties) so JsonUtility on the client and
// System.Text.Json (IncludeFields) on the server read and write the same shape.
namespace MobileIdleBuilder
{
    // ── Config (game_data.json game_config.pve → GameConfigSO.pve; the module embeds game_data.json) ──

    /// <summary>One reward band. Matches rank &lt;= rank_to, or the top top_percent% when rank_to is 0; both 0 = everyone left.</summary>
    [Serializable]
    public class PveRewardBand
    {
        public int rank_to;
        public int top_percent;
        public int crystals;
        public int stars;
        public int strings;         // Strings, the tournament currency (#500); spent in the tournament shop
    }

    [Serializable]
    public class PveLeagueDef
    {
        public string id;
        public string name;
        public int bracket_size;
        public int promote;
        public int demote;
        public PveRewardBand[] rewards;
    }

    [Serializable]
    public class PveConfig
    {
        public int run_hours = 24;
        public int test_bracket_size = 5;
        public int snapshot_bucket_minutes = 10;
        /// <summary>Highest league players can reach (a league id); blank = the whole ladder. Not set in game_data.json:
        /// the server fills it per environment from pve_settings maxLeague (<see cref="PveRules.WithCap"/>), so the dev
        /// environment can cap at "bronze" while production keeps the full ladder.</summary>
        public string max_league = "";
        public PveLeagueDef[] leagues;
    }

    // ── Server state (Cloud Save private custom items + protected player data) ──

    /// <summary>One net worth sample: t = seconds since the player's start, rate = idle net worth per second, cap = idle cap seconds.</summary>
    [Serializable]
    public class PveSnap
    {
        public int t;
        public double nw;
        public double rate;
        public int cap;
    }

    [Serializable]
    public class PveMember
    {
        public string playerId;
        public string name;
        public string startUtc;
        public bool dev;
        public bool retired;            // left early (#505): score frozen at the retire snapshot (rate 0), no more snapshots
        public string nameColor;        // cosmetics (#500): PveCosmetics ids, "" = none
        public string title;
        public List<PveSnap> snaps = new List<PveSnap>();
    }

    [Serializable]
    public class PveBracketMeta
    {
        public string id;
        public string week;
        public string league;
        public bool dev;
        public int capacity;
        public List<string> playerIds = new List<string>();
        public bool settled;
    }

    [Serializable]
    public class PveLeagueCursor
    {
        public string key;          // league id, or league id + "_dev" for dev-console saves
        public int brackets;        // brackets opened so far (ids 1..brackets)
        public int openCount;       // members in the newest bracket
        public int openCapacity;
    }

    [Serializable]
    public class PveWeekIndex
    {
        public string week;
        public List<PveLeagueCursor> cursors = new List<PveLeagueCursor>();
        public bool settled;
    }

    [Serializable]
    public class PveHistoryEntry
    {
        public string week;
        public string league;
        public int rank;
        public int size;
        public double score;
        public string result;       // promoted | demoted | stayed
        public string newLeague;
    }

    [Serializable]
    public class PveReward
    {
        public string week;
        public string league;
        public int rank;
        public int size;
        public int crystals;
        public int stars;
        public int strings;         // #500
    }

    [Serializable]
    public class PveProfile
    {
        public string league = PveRules.StartLeague;
        public string lastWeek;     // week id of the last tournament entered
        public string bracketId;    // that tournament's bracket
        public List<PveHistoryEntry> history = new List<PveHistoryEntry>();
        public List<PveReward> pendingRewards = new List<PveReward>();
        public string nameColor;    // the leaderboard look (#500), copied onto each new member record
        public string title;
    }

    // ── Responses (module → client, as a JSON string) ──

    [Serializable]
    public class PveStatusResponse
    {
        public string error;            // null/"" = ok; otherwise a PveErrors code
        public string serverNowUtc;
        public string week;
        public string phase;            // PvePhase name: Entry | Running | Results
        public string phaseEndsUtc;
        public string nextEntryUtc;
        public PveProfile profile;
        public bool joinedThisWeek;
        public string startUtc;
        public string runEndsUtc;
        public string bracketId;
        public bool bracketSettled;
        public bool retired;            // this player left this week's run early
    }

    [Serializable]
    public class PveSnapshotResponse
    {
        public string error;
        public int t;
        public bool accepted;
    }

    [Serializable]
    public class PveBoardRow
    {
        public int rank;
        public string name;
        public double value;
        public bool projected;
        public bool you;
        public bool retired;            // left early: the value is final
        public int elapsed;             // that player's own elapsed seconds (clamped to the run)
        public string nameColor;        // cosmetics (#500): PveCosmetics ids
        public string title;
    }

    [Serializable]
    public class PveBoardResponse
    {
        public string error;
        public string week;
        public string league;
        public int viewerT;
        public bool settled;
        public int promote;             // top N move up at settlement (scaled for this bracket's size)
        public int demote;              // bottom N move down
        public List<PveBoardRow> rows = new List<PveBoardRow>();
    }

    [Serializable]
    public class PveClaimResponse
    {
        public string error;
        public PveReward reward;
    }

    /// <summary>Error codes the module returns in <c>error</c>.</summary>
    /// <summary>DeleteMyData: every Cloud Save key of the caller is gone and they are off their bracket.</summary>
    [Serializable]
    public class PveDeleteResponse
    {
        public string error;            // null/"" = ok
        public bool deleted;
    }

    public static class PveErrors
    {
        public const string OutsideWindow   = "outside_window";
        public const string AlreadyJoined   = "already_joined";
        public const string NameRequired    = "name_required";
        public const string NameBlocked     = "name_blocked";
        public const string NotJoined       = "not_joined";
        public const string RunOver         = "run_over";
        public const string Rejected        = "snapshot_rejected";
        public const string Busy            = "busy";
        public const string NothingToClaim  = "nothing_to_claim";
        public const string Config          = "config_missing";
        public const string Retired         = "retired";
        public const string BadLook         = "bad_look";
    }
}
