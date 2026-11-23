using System;

namespace MobileIdleBuilder
{
    /// <summary>What the Tournament panel shows.</summary>
    public enum TournamentPanelState
    {
        Disabled,   // pve.enabled is off
        Locked,     // the Chemistry tutorial isn't finished yet
        Offline,    // no server status yet (offline or not signed in)
        Waiting,    // entry closed and not entered this week: countdown to Friday
        Open,       // entry open and not entered: explainer + Start
        Running,    // in the 24h run (or its prestige is still pending)
        Finished,   // run over, results not settled yet
        Results,    // settled: rank, league move, reward
    }

    /// <summary>
    /// Client-side tournament rules (PvE phase 4). Pure over the save and the times passed in, so they are
    /// unit-tested; <see cref="TournamentService"/> applies them. The server re-checks everything that matters.
    /// </summary>
    public static class TournamentRules
    {
        public const string UnlockWorld = "world_chemistry";

        /// <summary>Seconds of play between automatic snapshots.</summary>
        public const float SnapshotIntervalSeconds = 300f;

        /// <summary>Tournaments open once the Chemistry tutorial is finished.</summary>
        public static bool IsUnlocked(SaveData save) =>
            save != null && (TutorialFlows.Slot(save, UnlockWorld, create: false)?.done ?? false);

        static DateTime? Parse(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return null;
            try { return PveRules.ParseIso(iso); }
            catch (FormatException) { return null; }
        }

        public static DateTime? RunEnds(TournamentSaveData t) => Parse(t?.runEndsUtc);
        public static DateTime? RunStart(TournamentSaveData t) => Parse(t?.startUtc);

        /// <summary>The join's prestige hasn't happened yet (see <see cref="TournamentSaveData.prestigeCountAtJoin"/>).</summary>
        public static bool PrestigePending(TournamentSaveData t, int prestigeCount) =>
            t != null && t.prestigeCountAtJoin >= 0 && prestigeCount <= t.prestigeCountAtJoin;

        /// <summary>Inside the 24h run (from the join until the run ends).</summary>
        public static bool InRun(TournamentSaveData t, DateTime nowUtc)
        {
            var end = RunEnds(t);
            return end.HasValue && nowUtc < end.Value;
        }

        /// <summary>Manual prestige (and Ascension) is locked during the run, so net worth can't be reset mid-score.</summary>
        public static bool PrestigeLocked(TournamentSaveData t, DateTime nowUtc) => InRun(t, nowUtc);

        public static TimeSpan Remaining(TournamentSaveData t, DateTime nowUtc)
        {
            var end = RunEnds(t);
            if (!end.HasValue) return TimeSpan.Zero;
            var left = end.Value - nowUtc;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }

        /// <summary>
        /// Whether a snapshot should be sent now: during the run, never while the join's prestige is pending;
        /// every <see cref="SnapshotIntervalSeconds"/> of play (or at once when <paramref name="force"/>); and
        /// once more, the final one, in the grace window after the 24h mark.
        /// </summary>
        public static bool ShouldSnapshot(TournamentSaveData t, DateTime nowUtc, int prestigeCount,
                                          float secondsSinceLast, bool force)
        {
            if (t == null || PrestigePending(t, prestigeCount)) return false;
            var end = RunEnds(t);
            if (!end.HasValue) return false;
            if (nowUtc < end.Value) return force || secondsSinceLast >= SnapshotIntervalSeconds;
            // After the mark: one final snapshot while the server still accepts it.
            return !t.finalSent && nowUtc <= end.Value.AddSeconds(PveRules.FinalGraceSeconds);
        }

        /// <summary>Panel state from the flag, the unlock, the last server status and the local copy.</summary>
        public static TournamentPanelState PanelState(bool enabled, bool unlocked, PveStatusResponse status,
                                                      TournamentSaveData t, DateTime nowUtc)
        {
            if (!enabled) return TournamentPanelState.Disabled;
            if (!unlocked) return TournamentPanelState.Locked;
            if (InRun(t, nowUtc)) return TournamentPanelState.Running;   // works offline too
            if (status == null || !string.IsNullOrEmpty(status.error)) return TournamentPanelState.Offline;

            // Entered this week and the run is over: waiting for settlement, or settled.
            if (status.joinedThisWeek)
                return status.bracketSettled ? TournamentPanelState.Results : TournamentPanelState.Finished;
            // Not entered this week: open while entry is, else a countdown to Friday (the panel also shows the
            // last tournament's result from the profile history).
            return status.phase == nameof(PvePhase.Entry) ? TournamentPanelState.Open : TournamentPanelState.Waiting;
        }

        /// <summary>"14h 05m", "6m 30s".</summary>
        public static string FormatSpan(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            if (t.TotalDays >= 1) return $"{(int)t.TotalDays}d {t.Hours}h";
            if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes:D2}m";
            return $"{t.Minutes}m {t.Seconds:D2}s";
        }

        /// <summary>The prestige button's text while locked.</summary>
        public static string PrestigeLockText(TimeSpan remaining) => $"🔒 Tournament {FormatSpan(remaining)}";

        /// <summary>The confirm card's message before joining.</summary>
        public static string JoinConfirmMessage(long prestigeGain, string leagueName) =>
            $"Starting the tournament prestiges your current run (+{prestigeGain:N0} ✦). Then you have 24 hours to " +
            $"build the highest net worth you can against the other players in your {leagueName} bracket. " +
            "Prestige stays locked until the 24 hours are up.";

        public static string LeagueName(PveConfig cfg, string leagueId) =>
            PveRules.League(cfg, leagueId)?.name ?? (string.IsNullOrEmpty(leagueId) ? "Bronze" : leagueId);

        /// <summary>Player-facing text for a server error code.</summary>
        public static string ErrorText(string error)
        {
            if (string.IsNullOrEmpty(error)) return "";
            if (error.StartsWith("network:")) return "Couldn't reach the tournament server. Check your connection.";
            return error switch
            {
                PveErrors.OutsideWindow  => "Entry is closed. Tournaments open every Friday (00:00 UTC).",
                PveErrors.AlreadyJoined  => "You've already entered this week's tournament.",
                PveErrors.NameRequired   => "Choose a player name first (Settings > Account).",
                PveErrors.NameBlocked    => "Your player name isn't allowed. Change it in Settings > Account.",
                PveErrors.NotJoined      => "You haven't entered a tournament yet.",
                PveErrors.Config         => "Tournaments are unavailable right now.",
                _                        => "Something went wrong. Try again.",
            };
        }
    }
}
