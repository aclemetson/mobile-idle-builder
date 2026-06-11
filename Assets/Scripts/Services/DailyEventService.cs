using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Singleton retention service: 28-day login reward calendar + 3 rotating daily challenges.
    ///
    /// Time logic is UTC throughout, mirroring AchievementService.CheckPeriodResets():
    ///   - Challenges reset at 00:00 UTC; a new set of 3 is drawn deterministically from the pool.
    ///   - The login calendar advances only when the player CLAIMS (kindness rule — a missed day
    ///     never resets the streak; the index simply does not advance). The calendar loops after day 28.
    ///
    /// Reward grants reuse existing infrastructure: crystals → SaveData.paidCurrency,
    /// entropy → ECSLoadBridge.AddEntropy, prestige currency → ECSLoadBridge.AddPrestigeCurrency.
    ///
    /// State survives prestige (these SaveData fields are not cleared by PrestigeSystem).
    /// Core logic methods take an injected <c>now</c> so EditMode tests can drive them deterministically.
    /// </summary>
    public class DailyEventService : SingletonMonoBehaviour<DailyEventService>
    {
        protected override bool PersistAcrossScenes => false;

        [SerializeField] DailyContentSO content;

        /// <summary>Fired whenever streak/challenge state changes so the panel can refresh.</summary>
        public event Action OnChanged;

        // Runtime mirror of the daily-challenge SaveData fields, rebuilt on load.
        readonly Dictionary<string, int> _challengeProgress = new();
        readonly List<string>            _todaysChallengeIds = new();
        readonly HashSet<string>         _claimedChallenges  = new();

        public DailyContentSO Content => content;

        void Start() => EnsureToday();

        /// <summary>
        /// Ensures content is loaded, in-memory state is current, and today's challenge set is rolled.
        /// Safe to call repeatedly (e.g. each time the panel opens) — only re-rolls when expired, empty,
        /// or stale. Call this from the UI rather than relying solely on Start(), so the panel survives
        /// init-order races where SaveManager was not yet ready when this service's Start() ran.
        /// </summary>
        public void EnsureToday()
        {
            EnsureContent();
            if (_todaysChallengeIds.Count == 0)
                ReloadFromSave();
            CheckDailyReset(DateTime.UtcNow);
        }

        void EnsureContent()
        {
            if (content == null)
                content = Resources.Load<DailyContentSO>("DailyContent");
            if (content == null)
                GameLogger.Warning("[DailyEvents] DailyContent asset not found in Resources — daily events disabled.");
            else if (content.challengePool == null || content.challengePool.Length == 0)
                GameLogger.Warning("[DailyEvents] DailyContent has no challenge pool — run MobileIdleBuilder > Import Game Data.");
        }

        // ── Load / flush ──────────────────────────────────────────────────────

        /// <summary>Rebuilds in-memory challenge state from SaveData. Public for tests.</summary>
        public void ReloadFromSave()
        {
            _challengeProgress.Clear();
            _todaysChallengeIds.Clear();
            _claimedChallenges.Clear();

            var save = SaveManager.Instance?.Current;
            if (save == null) return;

            foreach (var id in save.dailyChallengeIds)
                _todaysChallengeIds.Add(id);
            foreach (var entry in save.dailyChallengeProgress)
                _challengeProgress[entry.id] = entry.count;
            foreach (var id in save.dailyChallengesClaimed)
                _claimedChallenges.Add(id);
        }

        void FlushToSave()
        {
            var save = SaveManager.Instance?.Current;
            if (save == null) return;

            save.dailyChallengeIds = new List<string>(_todaysChallengeIds);
            save.dailyChallengeProgress = _challengeProgress
                .Select(kvp => new AchievementProgressEntry { id = kvp.Key, count = kvp.Value })
                .ToList();
            save.dailyChallengesClaimed = new List<string>(_claimedChallenges);
        }

        // ── Daily challenge reset ─────────────────────────────────────────────

        /// <summary>
        /// Rolls a fresh set of 3 challenges if the current set has expired (00:00 UTC) or none exist.
        /// Copies AchievementService.CheckPeriodResets()'s ParseOrEpoch / NextMidnightUtc pattern.
        /// </summary>
        public void CheckDailyReset(DateTime now)
        {
            var save = SaveManager.Instance?.Current;
            if (save == null) return;

            var nextReset = ParseOrEpoch(save.dailyChallengeResetUtc);
            // Re-roll when expired, empty, or stale (stored ids no longer exist in the current pool,
            // e.g. the content asset changed since the set was saved).
            bool stale     = _todaysChallengeIds.Count > 0 && _todaysChallengeIds.Any(id => GetChallenge(id) == null);
            bool needsRoll = now >= nextReset || _todaysChallengeIds.Count == 0 || stale;
            if (!needsRoll) return;

            RollChallenges(now);
            save.dailyChallengeResetUtc = NextMidnightUtc(now).ToString("o");
            FlushToSave();
            OnChanged?.Invoke();
        }

        void RollChallenges(DateTime now)
        {
            _todaysChallengeIds.Clear();
            _challengeProgress.Clear();
            _claimedChallenges.Clear();

            var pool = content?.challengePool;
            if (pool == null || pool.Length == 0)
            {
                GameLogger.Warning("[DailyEvents] RollChallenges: challenge pool is empty — no challenges to draw.");
                return;
            }

            // Deterministic pick: seed the rotation by UTC day-of-year, take 3 consecutive (wrapping).
            int take   = Mathf.Min(3, pool.Length);
            int offset = now.DayOfYear % pool.Length;
            for (int i = 0; i < take; i++)
            {
                var entry = pool[(offset + i) % pool.Length];
                if (entry != null && !string.IsNullOrEmpty(entry.id))
                    _todaysChallengeIds.Add(entry.id);
            }

            string ids = string.Join(", ", _todaysChallengeIds);
            GameLogger.Debug($"[DailyEvents] Rolled {_todaysChallengeIds.Count} challenges for UTC day {now.DayOfYear}: {ids}");
        }

        // ── Challenge progress (forwarded from AchievementService.Notify*) ─────

        /// <summary>Call when the player crafts items.</summary>
        public void NotifyCraft(string itemId, int quantity = 1) =>
            Accumulate(AchievementTrigger.CraftItem, quantity);

        /// <summary>Call when the player places a building.</summary>
        public void NotifyBuildingPlaced(string buildingType) =>
            Accumulate(AchievementTrigger.PlaceBuilding, 1);

        /// <summary>Call when the player completes a research project.</summary>
        public void NotifyResearchCompleted(string researchId) =>
            Accumulate(AchievementTrigger.CompleteResearch, 1);

        /// <summary>Call whenever entropy is spent; accumulates the amount (not the call count).</summary>
        public void NotifyEntropySpent(long amount) =>
            Accumulate(AchievementTrigger.SpendEntropy, (int)Mathf.Min(amount, int.MaxValue));

        /// <summary>Call once per session start; auto-progresses the "log in" challenge.</summary>
        public void NotifyLogin() =>
            Accumulate(AchievementTrigger.Login, 1);

        void Accumulate(AchievementTrigger trigger, int delta)
        {
            if (content == null || _todaysChallengeIds.Count == 0 || delta <= 0) return;

            string triggerName = trigger.ToString();
            bool dirty = false;
            foreach (var id in _todaysChallengeIds)
            {
                if (_claimedChallenges.Contains(id)) continue;
                var entry = GetChallenge(id);
                if (entry == null) continue;
                if (!string.Equals(entry.trigger, triggerName, StringComparison.OrdinalIgnoreCase)) continue;

                _challengeProgress.TryGetValue(id, out int current);
                int updated = Mathf.Min(current + delta, entry.target); // cap at target
                if (updated != current)
                {
                    _challengeProgress[id] = updated;
                    dirty = true;
                }
            }

            if (dirty)
            {
                FlushToSave();
                OnChanged?.Invoke();
            }
        }

        /// <summary>Grants the crystal reward for a completed challenge. Returns false if not claimable.</summary>
        public bool ClaimChallenge(string id)
        {
            if (!_todaysChallengeIds.Contains(id) || _claimedChallenges.Contains(id)) return false;

            var entry = GetChallenge(id);
            if (entry == null || GetChallengeProgress(id) < entry.target) return false;

            var save = SaveManager.Instance?.Current;
            if (save == null) return false;

            _claimedChallenges.Add(id);
            if (entry.crystals > 0) save.paidCurrency += entry.crystals;

            FlushToSave();
            SaveManager.Instance.SaveLocal();
            OnChanged?.Invoke();
            GameLogger.Info($"[DailyEvents] Challenge '{id}' claimed — +{entry.crystals} crystals.");
            return true;
        }

        // ── Login reward calendar ─────────────────────────────────────────────

        /// <summary>0-based position in the 28-day calendar.</summary>
        public int LoginStreakIndex => SaveManager.Instance?.Current?.loginStreakIndex ?? 0;

        /// <summary>The reward currently available to claim (the entry at the streak index).</summary>
        public DailyRewardEntry CurrentLoginReward
        {
            get
            {
                var rewards = content?.loginRewards;
                if (rewards == null || rewards.Length == 0) return null;
                return rewards[LoginStreakIndex % rewards.Length];
            }
        }

        public bool CanClaimLoginReward() => CanClaimLoginReward(DateTime.UtcNow);

        /// <summary>True if today's (UTC) login reward has not been claimed yet.</summary>
        public bool CanClaimLoginReward(DateTime now)
        {
            var save = SaveManager.Instance?.Current;
            if (save == null) return false;
            if (content?.loginRewards == null || content.loginRewards.Length == 0) return false;

            var last = ParseOrEpoch(save.lastLoginRewardUtc);
            // Compare dates, not timestamps, to avoid double-grant within the same UTC day.
            return last == DateTime.MinValue || last.Date < now.Date;
        }

        public bool ClaimLoginReward() => ClaimLoginReward(DateTime.UtcNow);

        /// <summary>
        /// Claims today's login reward, grants it, advances the streak index (looping after the
        /// last calendar day), and records the claim date. Returns false if already claimed today.
        /// </summary>
        public bool ClaimLoginReward(DateTime now)
        {
            if (!CanClaimLoginReward(now)) return false;

            var save    = SaveManager.Instance.Current;
            var rewards = content.loginRewards;
            int idx     = save.loginStreakIndex % rewards.Length;
            var reward  = rewards[idx];

            GrantReward(save, reward.crystals, reward.entropy, reward.prestigeCurrency);

            save.loginStreakIndex   = (save.loginStreakIndex + 1) % rewards.Length;
            save.lastLoginRewardUtc = now.Date.ToString("o");

            SaveManager.Instance.SaveLocal();
            OnChanged?.Invoke();
            GameLogger.Info(
                $"[DailyEvents] Login reward day {reward.day} claimed — " +
                $"+{reward.crystals} crystals, +{reward.entropy} entropy, +{reward.prestigeCurrency} PC.");
            return true;
        }

        void GrantReward(SaveData save, int crystals, long entropy, int prestigeCurrency)
        {
            if (crystals > 0)         save.paidCurrency += crystals;
            if (entropy  > 0)         ECSLoadBridge.Instance?.AddEntropy(entropy);
            if (prestigeCurrency > 0) ECSLoadBridge.Instance?.AddPrestigeCurrency(prestigeCurrency);
        }

        // ── Query API (for the panel) ─────────────────────────────────────────

        public IReadOnlyList<string> TodaysChallengeIds => _todaysChallengeIds;
        public DailyChallengeEntry   GetChallengeEntry(string id) => GetChallenge(id);
        public int  GetChallengeProgress(string id) => _challengeProgress.TryGetValue(id, out var c) ? c : 0;
        public bool IsChallengeClaimed(string id)   => _claimedChallenges.Contains(id);

        public bool IsChallengeComplete(string id)
        {
            var entry = GetChallenge(id);
            return entry != null && GetChallengeProgress(id) >= entry.target;
        }

        /// <summary>Completed-but-unclaimed challenges (for a nav badge / "claim all" affordance).</summary>
        public int ClaimableChallengeCount =>
            _todaysChallengeIds.Count(id => IsChallengeComplete(id) && !IsChallengeClaimed(id));

        // ── Dev / lifecycle helpers ───────────────────────────────────────────

        /// <summary>Clears in-memory state after a dev "clear save" so it matches the reset SaveData.</summary>
        public void ResetInMemory()
        {
            _challengeProgress.Clear();
            _todaysChallengeIds.Clear();
            _claimedChallenges.Clear();
            GameLogger.Debug("[DailyEvents] In-memory state cleared by ResetInMemory().");
        }

        // ── Internals ─────────────────────────────────────────────────────────

        DailyChallengeEntry GetChallenge(string id)
        {
            var pool = content?.challengePool;
            if (pool == null || string.IsNullOrEmpty(id)) return null;
            foreach (var c in pool)
                if (c != null && c.id == id) return c;
            return null;
        }

        static DateTime ParseOrEpoch(string iso) =>
            DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
                ? dt : DateTime.MinValue;

        static DateTime NextMidnightUtc(DateTime from) =>
            from.Date.AddDays(1); // 00:00 UTC tomorrow
    }
}
