using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Singleton service that evaluates achievement triggers, tracks per-achievement
    /// progress, handles Daily/Weekly/Monthly period resets, manages claimable currency
    /// rewards, and keeps SaveManager in sync.
    ///
    /// Trigger flow:
    ///   Gameplay code calls Notify*() → Evaluate() checks matching AchievementSOs →
    ///   Complete() fires OnAchievementUnlocked + adds to unclaimedAchievements →
    ///   Player calls ClaimReward() → currencies credited, OnRewardClaimed fires.
    ///
    /// The whole feature is dormant until the player's first prestige: Evaluate() returns
    /// early while tutorial.hasCompletedFirstRun is false, so nothing accrues, completes,
    /// or reaches save. HUDController flips the gate and calls InitPostPrestige() when the
    /// first prestige lands mid-session.
    ///
    /// Loading: saved state is read once the save is final (ECSLoadBridge.IsLoaded, which follows the
    /// cloud check), not at scene start. Events before then are held and replayed.
    ///
    /// Period reset:
    ///   RequestPeriodCheck() runs on load, on resume from background, and while the menu is open.
    ///   It compares SERVER time (ServerClock, not the phone's clock) to the stored deadlines; an
    ///   expired period wipes that category's completed/unclaimed/progress and stores the next
    ///   deadline. With no server time (offline) nothing resets. Every decision is logged under
    ///   "[Achievements]" for on-device diagnosis.
    /// </summary>
    public class AchievementService : SingletonMonoBehaviour<AchievementService>
    {
        protected override bool PersistAcrossScenes => false;

        [SerializeField] AchievementDatabase database;
        [SerializeField] HUDController       hudController;

        /// <summary>Fired whenever a new achievement is completed (before claiming).</summary>
        public event Action<AchievementSO> OnAchievementUnlocked;

        /// <summary>Fired when a player claims the reward for a completed achievement.</summary>
        public event Action<AchievementSO> OnRewardClaimed;

        // Runtime state — rebuilt from save on Start
        readonly Dictionary<string, int> _progress    = new();
        readonly HashSet<string>         _completed   = new();
        readonly HashSet<string>         _unclaimed   = new();

        bool _initialized;   // LoadFromSave has run
        bool _ready;         // ...and the first period check has run; events are evaluated from here on

        // Period checks need server time. One sync at a time; after a failure, timer-driven checks wait.
        bool  _syncing;
        float _lastSyncFailedAt = float.NegativeInfinity;
        const float SyncRetrySeconds = 30f;

        // Diagnostics: the save object state was loaded from, to spot it being swapped underneath us.
        SaveData _loadedFrom;
        bool     _warnedSaveReplaced;

        /// <summary>Fired after a period reset re-locks achievements, so an open menu can rebuild.</summary>
        public event Action OnPeriodReset;

        /// <summary>
        /// Achievements stay entirely dormant until the player's first prestige: no progress
        /// accrues, nothing completes, and no toast fires. Gating only the menu is not enough —
        /// progress recorded during the tutorial would pop a "🏆 Achievement unlocked" toast over
        /// the tutorial and write itself to save before the feature is meant to exist.
        /// </summary>
        static bool AchievementsUnlocked =>
            SaveManager.Instance?.Current?.tutorial.hasCompletedFirstRun ?? false;

        // Achievement events that arrived before the save was final, replayed by Initialize.
        readonly List<(AchievementTrigger trigger, string targetId, int delta, bool absolute)> _pendingEvents = new();

        /// <summary>Longest wait for the save load before initializing anyway (the load has its own timeouts).</summary>
        internal const float SaveWaitTimeoutSeconds = 30f;

        void Start()
        {
            if (!AchievementsUnlocked)
            {
                GameLogger.Info("[Achievements] Dormant: first prestige not done (tutorial.hasCompletedFirstRun=false).");
                return;
            }
            if (IsSaveFinal) Initialize();   // nothing to wait for (no bridge, or already loaded)
            else             StartCoroutine(InitializeWhenSaveIsFinal());
        }

        static bool IsSaveFinal => ECSLoadBridge.Instance == null || ECSLoadBridge.Instance.IsLoaded;

        /// <summary>
        /// Waits for <see cref="ECSLoadBridge.IsLoaded"/> before loading state. The bridge only finishes after
        /// the cloud check, which can replace <see cref="SaveManager.Current"/> with the cloud save. Loading
        /// achievements at scene start, as before, read the save that was about to be replaced, and the next
        /// completion or claim wrote that stale state over the newer save, so a claimed daily or weekly could
        /// come back.
        /// </summary>
        System.Collections.IEnumerator InitializeWhenSaveIsFinal()
        {
            float waited = 0f;
            while (!IsSaveFinal && waited < SaveWaitTimeoutSeconds)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (IsSaveFinal)
                GameLogger.Info($"[Achievements] Save final after {waited:0.0}s; loading achievement state.");
            else
                GameLogger.Warning($"[Achievements] Save still NOT final after {waited:0.0}s; loading anyway. " +
                                   "If the cloud save replaces it later, achievement state will be stale.");
            Initialize();
        }

        /// <summary>
        /// Called when achievements unlock mid-session (first prestige).
        /// <see cref="Start"/> returned early before the flag was set, so this
        /// replicates the deferred initialization in the same session.
        /// </summary>
        public void InitPostPrestige() => Initialize();

        /// <summary>
        /// Loads saved state and applies period resets, exactly once per session. Guarded by an
        /// explicit flag rather than "is there any state yet?" — a freshly unlocked player has no
        /// completions and no progress, so a count-based guard cannot tell "not yet initialized"
        /// from "initialized and empty" and would skip <see cref="CheckPeriodResets"/>.
        /// </summary>
        void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            LoadFromSave();
            // Events stay held until this first period check is done, so today's progress is not counted
            // against yesterday's completions and then wiped by the reset.
            RequestPeriodCheck("load");
        }

        /// <summary>Replays events held while loading. Runs once, after the first period check (or its failure).</summary>
        void MarkReady()
        {
            if (_ready) return;
            _ready = true;

            if (_pendingEvents.Count == 0) return;
            GameLogger.Info($"[Achievements] Replaying {_pendingEvents.Count} event(s) held during load.");
            var pending = new List<(AchievementTrigger, string, int, bool)>(_pendingEvents);
            _pendingEvents.Clear();
            foreach (var (trigger, targetId, delta, absolute) in pending)
                Evaluate(trigger, targetId, delta, absolute);
        }

        /// <summary>True once saved state is loaded and the first period check has run (test seam).</summary>
        internal bool IsInitialized => _ready;

        // ── Load / flush ──────────────────────────────────────────────────────

        void LoadFromSave()
        {
            var save = SaveManager.Instance?.Current;
            if (save == null)
            {
                GameLogger.Warning("[Achievements] LoadFromSave: no SaveManager.Current; starting empty.");
                return;
            }
            _loadedFrom = save;

            _completed.Clear();
            _progress.Clear();
            _unclaimed.Clear();

            foreach (var id in save.achievements)
                _completed.Add(id);

            foreach (var entry in save.achievementProgress)
                _progress[entry.id] = entry.count;

            foreach (var id in save.unclaimedAchievements)
                _unclaimed.Add(id);

            GameLogger.Info($"[Achievements] Loaded from save (player={save.playerId}, lastSaved={save.lastSaved}): " +
                            $"completed {DescribeByCategory(_completed)}; unclaimed {DescribeByCategory(_unclaimed)}; " +
                            $"in progress {DescribeByCategory(_progress.Keys)}; deadlines daily={Show(save.dailyResetUtc)} " +
                            $"weekly={Show(save.weeklyResetUtc)} monthly={Show(save.monthlyResetUtc)}");
        }

        void FlushToSave(bool resetting = false)
        {
            var save = SaveManager.Instance?.Current;
            if (save == null) return;

            // Diagnostics for "achievements came back / disappeared": the save we loaded from was swapped
            // (cloud pick after load), or this flush removes completions that nothing here reset.
            if (!ReferenceEquals(save, _loadedFrom) && !_warnedSaveReplaced)
            {
                _warnedSaveReplaced = true;
                GameLogger.Warning($"[Achievements] SaveManager.Current was REPLACED after achievements loaded " +
                                   $"(loaded lastSaved={_loadedFrom?.lastSaved}, now lastSaved={save.lastSaved}). " +
                                   $"Writing in-memory state over the new save: completed {DescribeByCategory(_completed)}; " +
                                   $"new save had {save.achievements.Count} completed.");
            }
            if (!resetting)
            {
                var dropped = save.achievements.Where(id => !_completed.Contains(id)).ToList();
                if (dropped.Count > 0)
                    GameLogger.Warning($"[Achievements] Flush is removing {dropped.Count} completed achievement(s) " +
                                       $"outside a period reset: {string.Join(", ", dropped)}");
            }

            save.achievements        = new List<string>(_completed);
            save.achievementProgress = _progress
                .Select(kvp => new AchievementProgressEntry { id = kvp.Key, count = kvp.Value })
                .ToList();
            save.unclaimedAchievements = new List<string>(_unclaimed);
        }

        // ── Period reset ──────────────────────────────────────────────────────

        /// <summary>
        /// Runs the daily/weekly/monthly reset check against server time. Safe to call any number of
        /// times: on load, on resume from background, and when the achievements menu opens. With no
        /// server time yet it syncs first; if the sync fails, nothing resets (the device clock is not
        /// trusted, since moving it forward would re-arm dailies and pay out crystals again).
        /// </summary>
        public void RequestPeriodCheck(string reason)
        {
            if (!_initialized) return;

            var now = ServerClock.UtcNow;
            if (now.HasValue)
            {
                CheckPeriodResets(now.Value, reason);
                MarkReady();
                return;
            }

            if (_syncing) return;   // the running sync will do the check
            if (_ready && Time.realtimeSinceStartup - _lastSyncFailedAt < SyncRetrySeconds) return;

            _syncing = true;
            GameLogger.Info($"[Achievements] Period check ({reason}): fetching server time.");
            ServerClock.StartSync(this, ok =>
            {
                _syncing = false;
                var synced = ServerClock.UtcNow;
                if (ok && synced.HasValue)
                {
                    CheckPeriodResets(synced.Value, reason);
                }
                else
                {
                    _lastSyncFailedAt = Time.realtimeSinceStartup;
                    GameLogger.Warning($"[Achievements] Period check ({reason}) SKIPPED: no server time (offline?). " +
                                       $"Nothing resets until a sync succeeds. Device now {DateTime.UtcNow:o}.");
                }
                MarkReady();
            });
        }

        /// <summary>
        /// Cheap check for the open menu's 1 s tick: requests a period check only when a deadline has
        /// passed (or server time is missing and the retry window is open).
        /// </summary>
        public void CheckPeriodsIfDue()
        {
            if (!_ready) return;
            var save = SaveManager.Instance?.Current;
            if (save == null) return;

            var now = ServerClock.UtcNow;
            if (!now.HasValue) { RequestPeriodCheck("menu tick, no server time"); return; }

            if (IsDue(save.dailyResetUtc, now.Value) || IsDue(save.weeklyResetUtc, now.Value) ||
                IsDue(save.monthlyResetUtc, now.Value))
                RequestPeriodCheck("deadline passed while menu open");
        }

        static bool IsDue(string stored, DateTime now) => !TryParseUtc(stored, out var at) || now >= at;

        /// <summary>
        /// For each period: if its stored deadline has passed (or is missing), re-lock that category's
        /// achievements (bonus included), clear their progress and unclaimed rewards, and store the next
        /// deadline. Every decision is logged, because "achievements reset when they shouldn't" can only
        /// be diagnosed from what this saw on the device.
        /// </summary>
        void CheckPeriodResets(DateTime now, string reason)
        {
            var save = SaveManager.Instance?.Current;
            if (save == null) return;

            GameLogger.Info($"[Achievements] Period check ({reason}): server now {now:o}, device now {DateTime.UtcNow:o} " +
                            $"(device skew {ServerClock.FormatSkew(ServerClock.LastSkew)}), save lastSaved={save.lastSaved}");

            bool reset = false, dirty = false;
            CheckPeriod(AchievementCategory.Daily,   ref save.dailyResetUtc,   now, NextMidnightUtc,   ref reset, ref dirty);
            CheckPeriod(AchievementCategory.Weekly,  ref save.weeklyResetUtc,  now, NextMondayUtc,     ref reset, ref dirty);
            CheckPeriod(AchievementCategory.Monthly, ref save.monthlyResetUtc, now, NextMonthStartUtc, ref reset, ref dirty);

            if (dirty) FlushToSave(resetting: true);
            if (reset) OnPeriodReset?.Invoke();
        }

        void CheckPeriod(AchievementCategory category, ref string stored, DateTime now,
                         Func<DateTime, DateTime> nextDeadline, ref bool reset, ref bool dirty)
        {
            var fresh = nextDeadline(now);
            bool parsed = TryParseUtc(stored, out var deadline);

            if (parsed && now < deadline)
            {
                // A deadline later than a brand-new period's was written from a clock running ahead (an old
                // device-clock save, or a moved phone date). Pull it back without resetting, or the period
                // would be stuck until that future date.
                if (deadline > fresh)
                {
                    GameLogger.Warning($"[Achievements]   {category}: deadline {deadline:o} is past the current period's end " +
                                       $"{fresh:o} (written from a fast clock); clamping, no reset.");
                    stored = fresh.ToString("o");
                    dirty = true;
                    return;
                }
                GameLogger.Info($"[Achievements]   {category}: keep (resets {deadline:o}, in {deadline - now:d\\.hh\\:mm\\:ss}).");
                return;
            }

            string why = parsed                     ? $"deadline {deadline:o} passed"
                       : string.IsNullOrEmpty(stored) ? "NO stored deadline (new save, or field lost)"
                       : $"UNPARSEABLE deadline '{stored}'";

            var cleared = ResetCategory(category);
            stored = fresh.ToString("o");
            reset = dirty = true;

            string msg = $"[Achievements]   {category}: RESET ({why}). Cleared {cleared}. Next deadline {stored}.";
            if (!parsed && cleared != "nothing") GameLogger.Warning(msg + " Progress existed without a deadline, which is suspicious.");
            else                                 GameLogger.Info(msg);
        }

        /// <summary>Re-locks one category; returns a log line of what it cleared.</summary>
        string ResetCategory(AchievementCategory category)
        {
            if (database == null) return "nothing (no database)";
            var completed = new List<string>();
            var unclaimed = new List<string>();
            var progress  = new List<string>();
            foreach (var a in database.All)
            {
                if (a == null || a.category != category) continue;
                if (_completed.Remove(a.id)) completed.Add(a.id);
                if (_unclaimed.Remove(a.id)) unclaimed.Add(a.id);
                if (_progress.TryGetValue(a.id, out int n) && _progress.Remove(a.id)) progress.Add($"{a.id}={n}");
            }
            if (completed.Count + unclaimed.Count + progress.Count == 0) return "nothing";
            return $"completed [{string.Join(", ", completed)}], UNCLAIMED [{string.Join(", ", unclaimed)}], " +
                   $"progress [{string.Join(", ", progress)}]";
        }

        /// <summary>Parses a stored ISO 8601 deadline as UTC, independent of the phone's culture.</summary>
        static bool TryParseUtc(string iso, out DateTime utc)
        {
            if (!string.IsNullOrEmpty(iso) &&
                DateTime.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture,
                                  System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            {
                utc = dt.Kind switch
                {
                    DateTimeKind.Local       => dt.ToUniversalTime(),
                    DateTimeKind.Unspecified => DateTime.SpecifyKind(dt, DateTimeKind.Utc),
                    _                        => dt
                };
                return true;
            }
            utc = default;
            return false;
        }

        // ── Diagnostics helpers ───────────────────────────────────────────────

        static string Show(string s) => string.IsNullOrEmpty(s) ? "<none>" : s;

        string DescribeByCategory(IEnumerable<string> ids)
        {
            var groups = ids
                .GroupBy(id => database?.Get(id)?.category.ToString() ?? "Unknown")
                .Select(g => $"{g.Key}: {string.Join(",", g)}")
                .ToList();
            return groups.Count == 0 ? "(none)" : "{" + string.Join(" | ", groups) + "}";
        }

        /// <summary>
        /// On resume the server clock may be stale (its stopwatch can stop while the phone sleeps), and a
        /// period may have ended while backgrounded; re-sync and check. A cold start goes through Initialize.
        /// </summary>
        void OnApplicationPause(bool paused)
        {
            if (paused || !_initialized) return;
            ServerClock.Invalidate();
            _lastSyncFailedAt = float.NegativeInfinity;
            RequestPeriodCheck("resume from background");
        }

        static DateTime NextMidnightUtc(DateTime from) =>
            from.Date.AddDays(1); // 00:00 UTC tomorrow

        static DateTime NextMondayUtc(DateTime from)
        {
            int daysUntilMonday = ((int)DayOfWeek.Monday - (int)from.DayOfWeek + 7) % 7;
            if (daysUntilMonday == 0) daysUntilMonday = 7;
            return from.Date.AddDays(daysUntilMonday);
        }

        static DateTime NextMonthStartUtc(DateTime from) =>
            new DateTime(from.Year, from.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);

        // ── Public trigger API ────────────────────────────────────────────────

        /// <summary>Call when the player crafts an item (manual or automated output).</summary>
        public void NotifyCraft(string itemId, int quantity = 1)
        {
            Evaluate(AchievementTrigger.CraftItem, itemId, delta: quantity);
            DailyEventService.Instance?.NotifyCraft(itemId, quantity);
        }

        /// <summary>Call when the player places a building.</summary>
        public void NotifyBuildingPlaced(string buildingType)
        {
            Evaluate(AchievementTrigger.PlaceBuilding, buildingType, delta: 1);
            DailyEventService.Instance?.NotifyBuildingPlaced(buildingType);
            TelemetryService.Instance?.RecordBuildingPlaced(buildingType);
        }

        /// <summary>Call when the player completes a research project.</summary>
        public void NotifyResearchCompleted(string researchId)
        {
            Evaluate(AchievementTrigger.CompleteResearch, researchId, delta: 1);
            DailyEventService.Instance?.NotifyResearchCompleted(researchId);
            TelemetryService.Instance?.RecordResearch(researchId);
        }

        /// <summary>Call when the player reaches a new tier (pass the absolute tier number).</summary>
        public void NotifyTierReached(int tier)
        {
            Evaluate(AchievementTrigger.ReachTier, tier.ToString(), delta: tier, absolute: true);
            TelemetryService.Instance?.RecordTier(tier);
        }

        /// <summary>Call when the player completes a prestige.</summary>
        public void NotifyPrestige() =>
            Evaluate(AchievementTrigger.Prestige, targetId: "", delta: 1);

        /// <summary>Call when the player wins a PVP match.</summary>
        public void NotifyPVPWin() =>
            Evaluate(AchievementTrigger.WinPVP, targetId: "", delta: 1);

        /// <summary>Call when the player unlocks a codex entry; pass the running total.</summary>
        public void NotifyCodexUnlocked(int totalCount) =>
            Evaluate(AchievementTrigger.UnlockCodex, targetId: "", delta: totalCount, absolute: true);

        /// <summary>Call once per game session start (e.g. from SaveManager or GameBootstrap).</summary>
        public void NotifyLogin()
        {
            Evaluate(AchievementTrigger.Login, targetId: "", delta: 1);
            DailyEventService.Instance?.NotifyLogin();
        }

        /// <summary>Call whenever entropy is spent; pass the amount spent this transaction.</summary>
        public void NotifyEntropySpent(long amount)
        {
            Evaluate(AchievementTrigger.SpendEntropy, targetId: "", delta: (int)Mathf.Min(amount, int.MaxValue));
            DailyEventService.Instance?.NotifyEntropySpent(amount);
        }

        /// <summary>Call when the player earns prestige currency; pass the amount earned this prestige.</summary>
        public void NotifyPrestigeCurrencyEarned(long amount) =>
            Evaluate(AchievementTrigger.TotalPrestigeCurrencyEarned, targetId: "",
                     delta: (int)Mathf.Min(amount, int.MaxValue));

        // ── Evaluation ────────────────────────────────────────────────────────

        /// <param name="absolute">
        /// When true, <paramref name="delta"/> replaces the stored count rather than adding to it.
        /// Used for tier and codex totals which are inherently monotonically increasing.
        /// </param>
        void Evaluate(AchievementTrigger trigger, string targetId, int delta, bool absolute = false)
        {
            if (database == null) return;
            // Single funnel for every Notify*() call, so this one check keeps the whole feature
            // dormant pre-prestige. CheckCategoryComplete is only reached from here.
            if (!AchievementsUnlocked) return;

            // Before the saved state is loaded, counting against empty sets would complete achievements the
            // player already has, and the flush would write those empty sets over the save. Hold the event.
            if (!_ready)
            {
                _pendingEvents.Add((trigger, targetId, delta, absolute));
                return;
            }

            bool dirty = false;
            // Track every category that had at least one completion this call
            var completedCategories = new HashSet<AchievementCategory>();

            foreach (var achievement in database.All)
            {
                if (achievement == null)                             continue;
                if (_completed.Contains(achievement.id))            continue;
                if (achievement.triggerType != trigger)             continue;
                if (!string.IsNullOrEmpty(achievement.triggerTargetId) &&
                    achievement.triggerTargetId != targetId)        continue;

                _progress.TryGetValue(achievement.id, out int current);
                int updated = absolute ? Mathf.Max(current, delta) : current + delta;
                _progress[achievement.id] = updated;
                dirty = true;

                if (updated >= achievement.triggerQuantity)
                {
                    Complete(achievement);
                    completedCategories.Add(achievement.category);
                }
            }

            if (dirty) FlushToSave();

            // After completing, check each affected category for a bonus unlock
            foreach (var cat in completedCategories)
                CheckCategoryComplete(cat);
        }

        void Complete(AchievementSO achievement)
        {
            _completed.Add(achievement.id);
            _progress.Remove(achievement.id);
            _unclaimed.Add(achievement.id);

            UnlockCosmetics(achievement);
            ReportToPlatform(achievement);

            string rewardHint = BuildRewardHint(achievement);
            string msg = string.IsNullOrEmpty(rewardHint)
                ? $"Achievement unlocked: {achievement.displayName}"
                : $"Achievement unlocked: {achievement.displayName} — {rewardHint}";

            // Tappable: opens the achievements menu on this one's tab; the check button claims it on the spot.
            hudController?.ShowAchievementNotification(achievement, msg);
            OnAchievementUnlocked?.Invoke(achievement);

            GameLogger.Info($"[Achievements] Unlocked: {achievement.id}");
        }

        static string BuildRewardHint(AchievementSO a)
        {
            var parts = new List<string>();
            if (a.paidCurrencyReward    > 0) parts.Add($"♦ {a.paidCurrencyReward}");
            if (a.prestigeCurrencyReward > 0) parts.Add($"⟳ {a.prestigeCurrencyReward} PC");
            return string.Join(", ", parts);
        }

        static void UnlockCosmetics(AchievementSO achievement)
        {
            if (achievement.rewards == null) return;
            foreach (var cosmetic in achievement.rewards)
            {
                if (cosmetic == null) continue;
                GameLogger.Info($"[Achievements] Cosmetic unlocked: {cosmetic.id} ({cosmetic.type})");
            }
        }

        static void ReportToPlatform(AchievementSO achievement)
        {
            if (string.IsNullOrEmpty(achievement.platformAchievementId)) return;
            // Google Play Games: PlayGamesPlatform.Instance.ReportProgress(...)
            // Apple Game Center: Social.ReportProgress(...)
            GameLogger.Debug($"[Achievements] Platform report stub: {achievement.platformAchievementId}");
        }

        // ── Category-complete bonus ───────────────────────────────────────────

        /// <summary>
        /// If all non-bonus achievements in <paramref name="category"/> are completed,
        /// auto-completes the category's bonus achievement (e.g. daily_complete).
        /// </summary>
        public void CheckCategoryComplete(AchievementCategory category)
        {
            if (database == null) return;

            string bonusId = CategoryBonusId(category);
            if (string.IsNullOrEmpty(bonusId) || _completed.Contains(bonusId)) return;

            bool allDone = database.All
                .Where(a => a != null && a.category == category && a.id != bonusId)
                .All(a => _completed.Contains(a.id));

            if (!allDone) return;

            var bonus = database.Get(bonusId);
            if (bonus != null)
            {
                Complete(bonus);
                FlushToSave();
            }
        }

        static string CategoryBonusId(AchievementCategory cat) => cat switch
        {
            AchievementCategory.Daily   => "daily_complete",
            AchievementCategory.Weekly  => "weekly_complete",
            AchievementCategory.Monthly => "monthly_complete",
            _                           => null
        };

        // ── Claiming API ──────────────────────────────────────────────────────

        /// <summary>
        /// Claims the pending reward for a completed achievement.
        /// Grants paidCurrencyReward and prestigeCurrencyReward, then fires OnRewardClaimed.
        /// </summary>
        public void ClaimReward(string id)
        {
            if (!_unclaimed.Contains(id)) return;

            var achievement = database?.Get(id);
            if (achievement == null) return;

            var save = SaveManager.Instance?.Current;
            if (save == null) return;

            _unclaimed.Remove(id);

            if (achievement.paidCurrencyReward > 0)
                save.paidCurrency += achievement.paidCurrencyReward;

            if (achievement.prestigeCurrencyReward > 0)
                save.prestigeCurrency += achievement.prestigeCurrencyReward;

            FlushToSave();
            OnRewardClaimed?.Invoke(achievement);

            GameLogger.Info($"[Achievements] Reward claimed: {id} — ◆{achievement.paidCurrencyReward}");
        }

        /// <summary>Claims all currently claimable achievement rewards at once.</summary>
        public void ClaimAllRewards()
        {
            foreach (var id in _unclaimed.ToList())
                ClaimReward(id);
        }

        public bool IsClaimable(string id)   => _unclaimed.Contains(id);
        public int  ClaimableCount           => _unclaimed.Count;

        /// <summary>True if any achievement in <paramref name="category"/> has a reward waiting (the tab's red dot).</summary>
        public bool HasClaimable(AchievementCategory category)
        {
            if (database == null) return false;
            foreach (var id in _unclaimed)
            {
                var a = database.Get(id);
                if (a != null && a.category == category) return true;
            }
            return false;
        }

        // ── Query API (for panel) ─────────────────────────────────────────────

        public IReadOnlyList<AchievementSO> GetAll()            => database?.All;
        public IEnumerable<AchievementSO> GetByCategory(AchievementCategory cat) =>
            database?.All.Where(a => a != null && a.category == cat) ?? Enumerable.Empty<AchievementSO>();
        public bool IsCompleted(string id)                      => _completed.Contains(id);
        public int  GetProgress(string id)                      => _progress.TryGetValue(id, out var c) ? c : 0;
        public int  CompletedCount                              => _completed.Count;

        /// <summary>
        /// Returns a human-readable string showing how long until the next reset for a category,
        /// e.g. "Resets in 4h 32m".
        /// </summary>
        public string GetResetCountdown(AchievementCategory category)
        {
            var save = SaveManager.Instance?.Current;
            if (save == null) return "";

            string raw = category switch
            {
                AchievementCategory.Daily   => save.dailyResetUtc,
                AchievementCategory.Weekly  => save.weeklyResetUtc,
                AchievementCategory.Monthly => save.monthlyResetUtc,
                _                           => null
            };

            if (!TryParseUtc(raw, out var resetAt))
                return "";

            var remaining = resetAt - (ServerClock.UtcNow ?? GameClock.UtcNow);
            if (remaining <= TimeSpan.Zero) return "Resetting…";

            if (remaining.TotalDays >= 1)
                return $"Resets in {(int)remaining.TotalDays}d {remaining.Hours}h";
            if (remaining.TotalHours >= 1)
                return $"Resets in {(int)remaining.TotalHours}h {remaining.Minutes}m";
            return $"Resets in {remaining.Minutes}m {remaining.Seconds}s";
        }

        // ── Dev console helpers ───────────────────────────────────────────────

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public void ForceComplete(string id)
        {
            var achievement = database?.Get(id);
            if (achievement == null || _completed.Contains(id)) return;
            Complete(achievement);
            FlushToSave();
        }

        public void ForceCompleteAll()
        {
            if (database == null) return;
            foreach (var a in database.All)
                if (a != null && !_completed.Contains(a.id))
                    Complete(a);
            FlushToSave();
        }
#endif

        /// <summary>
        /// Clears all in-memory achievement state. Called after a "clear save" dev command
        /// so in-memory state matches the freshly reset SaveData before the scene reloads.
        /// </summary>
        public void ResetInMemory()
        {
            _completed.Clear();
            _progress.Clear();
            _unclaimed.Clear();
            // A cleared save is pre-prestige again, so the service must be re-initializable.
            _initialized = false;
            _ready       = false;
            _loadedFrom  = null;
            _warnedSaveReplaced = false;
            GameLogger.Info("[Achievements] In-memory state cleared by ResetInMemory() (dev save wipe).");
        }
    }
}
