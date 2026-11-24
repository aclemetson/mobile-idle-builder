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

        /// <summary>Inside the 24h run (from the join until the run ends, or until the player retires).</summary>
        public static bool InRun(TournamentSaveData t, DateTime nowUtc)
        {
            if (t != null && t.retired) return false;
            var end = RunEnds(t);
            return end.HasValue && nowUtc < end.Value;
        }

        /// <summary>
        /// Prestiging (or Ascending) now would end the tournament run early (#505): the player is asked first and the
        /// run is retired on the server at the current net worth before the prestige goes through.
        /// </summary>
        public static bool PrestigeLeavesRun(TournamentSaveData t, DateTime nowUtc) => InRun(t, nowUtc);

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
            if (t == null || t.retired || PrestigePending(t, prestigeCount)) return false;
            var end = RunEnds(t);
            if (!end.HasValue) return false;
            if (nowUtc < end.Value) return force || secondsSinceLast >= SnapshotIntervalSeconds;
            // After the mark: one final snapshot while the server still accepts it.
            return !t.finalSent && nowUtc <= end.Value.AddSeconds(PveRules.FinalGraceSeconds);
        }

        /// <summary>Elapsed seconds into the run at <paramref name="nowUtc"/>, clamped to the run; null when not entered.</summary>
        public static int? Elapsed(TournamentSaveData t, DateTime nowUtc)
        {
            var start = RunStart(t); var end = RunEnds(t);
            if (!start.HasValue || !end.HasValue) return null;
            return PveRules.Elapsed(start.Value, nowUtc, (int)(end.Value - start.Value).TotalSeconds);
        }

        public static bool HasPending(TournamentSaveData t) => t != null && t.pendingT >= 0;

        /// <summary>
        /// Remembers a snapshot taken now as the one to deliver later if this send doesn't land. Only during the
        /// run and after the join's prestige (the device never keeps a post-24h value), and never older than
        /// the one already kept. Returns true when it was kept.
        /// </summary>
        public static bool CapturePending(TournamentSaveData t, DateTime nowUtc, int prestigeCount,
                                          double netWorth, double rate, int cap)
        {
            if (t == null || !InRun(t, nowUtc) || PrestigePending(t, prestigeCount)) return false;
            int? e = Elapsed(t, nowUtc);
            if (!e.HasValue || (t.pendingT >= 0 && e.Value < t.pendingT)) return false;
            t.pendingT = e.Value; t.pendingNw = netWorth; t.pendingRate = rate; t.pendingCap = cap;
            return true;
        }

        /// <summary>
        /// A delivered snapshot taken at device-elapsed <paramref name="deliveredT"/> supersedes the kept one
        /// unless the kept one is newer (captured while the send was in flight).
        /// </summary>
        public static void ClearPendingIfCovered(TournamentSaveData t, int deliveredT)
        {
            if (t != null && t.pendingT >= 0 && deliveredT >= t.pendingT) t.pendingT = -1;
        }

        /// <summary>
        /// Deliver the kept snapshot now? Once the run is over (during it the next live snapshot supersedes it)
        /// and the join's prestige has run.
        /// </summary>
        public static bool ShouldSendPending(TournamentSaveData t, DateTime nowUtc, int prestigeCount) =>
            HasPending(t) && !InRun(t, nowUtc) && !PrestigePending(t, prestigeCount);

        // ── Rewards ──────────────────────────────────────────────────────

        public const int ClaimedWeeksKept = 20;

        /// <summary>This save already received the reward for <paramref name="week"/>.</summary>
        public static bool RewardApplied(SaveData save, string week) =>
            save?.pveClaimedWeeks != null && !string.IsNullOrEmpty(week) && save.pveClaimedWeeks.Contains(week);

        /// <summary>
        /// Applies a tournament reward to the save once: crystals, then stars drawn from
        /// <paramref name="starPool"/> (discovered items), and records the week. Returns where each star landed;
        /// empty (and nothing changed) when the week was already applied.
        /// </summary>
        public static System.Collections.Generic.List<string> ApplyReward(SaveData save, PveReward reward,
            System.Collections.Generic.IReadOnlyList<string> starPool, Random rng)
        {
            var landed = new System.Collections.Generic.List<string>();
            if (save == null || reward == null || RewardApplied(save, reward.week)) return landed;
            if (reward.crystals > 0) save.paidCurrency += reward.crystals;
            if (reward.stars > 0) landed = ItemStars.Grant(save, reward.stars, starPool, rng);
            save.pveClaimedWeeks ??= new System.Collections.Generic.List<string>();
            save.pveClaimedWeeks.Add(reward.week);
            if (save.pveClaimedWeeks.Count > ClaimedWeeksKept)
                save.pveClaimedWeeks.RemoveRange(0, save.pveClaimedWeeks.Count - ClaimedWeeksKept);
            return landed;
        }

        /// <summary>The first reward still on the server that this save hasn't applied yet, or null.</summary>
        public static PveReward NextUnappliedReward(PveStatusResponse status, SaveData save)
        {
            var list = status?.profile?.pendingRewards;
            if (list == null) return null;
            foreach (var r in list) if (r != null && !RewardApplied(save, r.week)) return r;
            return null;
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

        /// <summary>The prestige button's text during a run: still available, with the run's time left as a reminder.</summary>
        public static string PrestigeInRunText(TimeSpan remaining) => $"⬆ Prestige · 🏆 {FormatSpan(remaining)}";

        /// <summary>The card shown before a prestige or Ascension ends the run.</summary>
        public static string PrestigeLeaveMessage(double netWorth, TimeSpan remaining) =>
            $"You are in a tournament run ({FormatSpan(remaining)} left). Prestiging now ends it early: your score " +
            $"stays at your current net worth, {NumberFormat.Entropy(netWorth)}, for the rest of the 24 hours.";

        /// <summary>The card shown before retiring from the Tournament panel.</summary>
        public static string RetireMessage(double netWorth, TimeSpan remaining) =>
            $"Retire from this tournament with {FormatSpan(remaining)} left? Your score stays at your current net worth, " +
            $"{NumberFormat.Entropy(netWorth)}, and prestige is yours to use again. Results still come at the end of the week.";

        /// <summary>The confirm card's message before joining.</summary>
        public static string JoinConfirmMessage(long prestigeGain, string leagueName) =>
            $"Starting the tournament prestiges your current run (+{prestigeGain:N0} ✦). Then you have 24 hours to " +
            $"build the highest net worth you can against the other players in your {leagueName} bracket. " +
            "Prestige stays locked until the 24 hours are up.";

        public static string LeagueName(PveConfig cfg, string leagueId) =>
            PveRules.League(cfg, leagueId)?.name ?? (string.IsNullOrEmpty(leagueId) ? "Bronze" : leagueId);

        /// <summary>
        /// A tournament time in the player's own time zone, with the weekday: "Friday 7:00 PM" (en-US). Tournament
        /// times are UTC on the server; the player sees them as their local time.
        /// </summary>
        public static string LocalTimeText(DateTime utc, TimeZoneInfo zone = null,
                                           System.Globalization.CultureInfo culture = null)
        {
            zone ??= TimeZoneInfo.Local;
            culture ??= System.Globalization.CultureInfo.CurrentCulture;
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
            return local.ToString("dddd", culture) + " " + local.ToString("t", culture);
        }

        /// <summary>When this week's entry closes, as local time (the weekly reminder dialogue's {entry_closes}).</summary>
        public static string EntryClosesLocal(DateTime nowUtc, TimeZoneInfo zone = null,
                                              System.Globalization.CultureInfo culture = null) =>
            LocalTimeText(PveRules.WeekStart(nowUtc).AddDays(1), zone, culture);

        /// <summary>When the next entry window opens, as local time.</summary>
        public static string NextOpensLocal(DateTime nowUtc, TimeZoneInfo zone = null,
                                            System.Globalization.CultureInfo culture = null) =>
            LocalTimeText(PveRules.NextEntry(nowUtc), zone, culture);

        /// <summary>Player-facing text for a server error code.</summary>
        public static string ErrorText(string error)
        {
            if (string.IsNullOrEmpty(error)) return "";
            if (error.StartsWith("network:")) return "Couldn't reach the tournament server. Check your connection.";
            return error switch
            {
                PveErrors.OutsideWindow  => $"Entry is closed. The next tournament opens {NextOpensLocal(TournamentService.Now)}.",
                PveErrors.AlreadyJoined  => "You've already entered this week's tournament.",
                PveErrors.NameRequired   => "Choose a player name first (Settings > Account).",
                PveErrors.NameBlocked    => "Your player name isn't allowed. Change it in Settings > Account.",
                PveErrors.NotJoined      => "You haven't entered a tournament yet.",
                PveErrors.Config         => "Tournaments are unavailable right now.",
                PveErrors.Retired        => "You already left this tournament.",
                PveErrors.RunOver        => "Your tournament run is already over.",
                _                        => "Something went wrong. Try again.",
            };
        }
    }
}
