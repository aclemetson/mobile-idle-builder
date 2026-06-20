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
    /// Period reset:
    ///   On Start(), CheckPeriodResets() compares current UTC date to stored reset
    ///   timestamps. If a period has expired the completed set and progress for that
    ///   category are wiped and the reset timestamp is advanced.
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

        void Start()
        {
            if (!(SaveManager.Instance?.Current?.tutorial.hasCompletedFirstRun ?? false))
                return;

            LoadFromSave();
            CheckPeriodResets();
        }

        /// <summary>
        /// Called when achievements unlock mid-session (first prestige).
        /// <see cref="Start"/> returned early before the flag was set, so this
        /// replicates the deferred initialization in the same session.
        /// </summary>
        public void InitPostPrestige()
        {
            if (_completed.Count > 0 || _progress.Count > 0) return; // already initialized
            LoadFromSave();
            CheckPeriodResets();
        }

        // ── Load / flush ──────────────────────────────────────────────────────

        void LoadFromSave()
        {
            var save = SaveManager.Instance?.Current;
            if (save == null) return;

            _completed.Clear();
            _progress.Clear();
            _unclaimed.Clear();

            foreach (var id in save.achievements)
                _completed.Add(id);

            foreach (var entry in save.achievementProgress)
                _progress[entry.id] = entry.count;

            foreach (var id in save.unclaimedAchievements)
                _unclaimed.Add(id);
        }

        void FlushToSave()
        {
            var save = SaveManager.Instance?.Current;
            if (save == null) return;

            save.achievements        = new List<string>(_completed);
            save.achievementProgress = _progress
                .Select(kvp => new AchievementProgressEntry { id = kvp.Key, count = kvp.Value })
                .ToList();
            save.unclaimedAchievements = new List<string>(_unclaimed);
        }

        // ── Period reset ──────────────────────────────────────────────────────

        /// <summary>
        /// Checks whether the daily, weekly, and monthly periods have expired.
        /// If a period has expired: completed achievements for that category are
        /// re-locked, in-progress counts are cleared, and a new reset timestamp
        /// is written. The bonus "all-complete" achievement is also re-locked.
        /// </summary>
        void CheckPeriodResets()
        {
            var save = SaveManager.Instance?.Current;
            if (save == null) return;

            var now = DateTime.UtcNow;
            bool dirty = false;

            // Daily — resets at 00:00 UTC each day
            var nextDaily = ParseOrEpoch(save.dailyResetUtc);
            if (now >= nextDaily)
            {
                ResetCategory(AchievementCategory.Daily);
                save.dailyResetUtc = NextMidnightUtc(now).ToString("o");
                dirty = true;
            }

            // Weekly — resets every Monday 00:00 UTC
            var nextWeekly = ParseOrEpoch(save.weeklyResetUtc);
            if (now >= nextWeekly)
            {
                ResetCategory(AchievementCategory.Weekly);
                save.weeklyResetUtc = NextMondayUtc(now).ToString("o");
                dirty = true;
            }

            // Monthly — resets on the 1st of each month 00:00 UTC
            var nextMonthly = ParseOrEpoch(save.monthlyResetUtc);
            if (now >= nextMonthly)
            {
                ResetCategory(AchievementCategory.Monthly);
                save.monthlyResetUtc = NextMonthStartUtc(now).ToString("o");
                dirty = true;
            }

            if (dirty) FlushToSave();
        }

        void ResetCategory(AchievementCategory category)
        {
            if (database == null) return;
            foreach (var a in database.All)
            {
                if (a == null || a.category != category) continue;
                _completed.Remove(a.id);
                _unclaimed.Remove(a.id);
                _progress.Remove(a.id);
            }
        }

        static DateTime ParseOrEpoch(string iso) =>
            DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
                ? dt : DateTime.MinValue;

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
        }

        /// <summary>Call when the player completes a research project.</summary>
        public void NotifyResearchCompleted(string researchId)
        {
            Evaluate(AchievementTrigger.CompleteResearch, researchId, delta: 1);
            DailyEventService.Instance?.NotifyResearchCompleted(researchId);
        }

        /// <summary>Call when the player reaches a new tier (pass the absolute tier number).</summary>
        public void NotifyTierReached(int tier) =>
            Evaluate(AchievementTrigger.ReachTier, tier.ToString(), delta: tier, absolute: true);

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

            hudController?.ShowNotification("🏆", msg);
            OnAchievementUnlocked?.Invoke(achievement);

            GameLogger.Info($"[Achievements] Unlocked: {achievement.id}");
        }

        static string BuildRewardHint(AchievementSO a)
        {
            var parts = new List<string>();
            if (a.paidCurrencyReward    > 0) parts.Add($"◆ {a.paidCurrencyReward}");
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

            if (!DateTime.TryParse(raw, null, System.Globalization.DateTimeStyles.RoundtripKind, out var resetAt))
                return "";

            var remaining = resetAt - DateTime.UtcNow;
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
            GameLogger.Debug("[Achievements] In-memory state cleared by ResetInMemory().");
        }
    }
}
