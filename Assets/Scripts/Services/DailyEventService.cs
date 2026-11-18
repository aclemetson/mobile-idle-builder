using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Singleton retention service: 28-day login reward calendar + 3 rotating daily challenges.
    ///
    /// Time logic is UTC throughout and uses SERVER time (ServerClock), like AchievementService:
    /// moving the phone's date must not re-roll challenges or re-open the login reward. With no
    /// server time (offline) nothing rolls and the login reward cannot be claimed until a sync succeeds.
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

        // Challenge events that arrived before today's set was checked against server time. Counting them
        // against yesterday's set would lose them when the roll clears progress, so they wait and replay.
        readonly List<(AchievementTrigger trigger, int delta)> _pendingEvents = new();
        bool _dayChecked;

        // Server-time sync: one at a time; after a failure, retries wait SyncRetrySeconds.
        bool  _syncing;
        float _lastSyncFailedAt = float.NegativeInfinity;
        const float SyncRetrySeconds       = 30f;
        const float SaveWaitTimeoutSeconds = 30f;
        bool  _warnedFutureLoginClaim;

        /// <summary>True while no server time is available; the panel says to connect instead of "claimed".</summary>
        public bool WaitingForServerTime => !ServerClock.IsSynced;

        void Start()
        {
            if (IsSaveFinal) EnsureToday();
            else             StartCoroutine(EnsureTodayWhenSaveIsFinal());
        }

        static bool IsSaveFinal => ECSLoadBridge.Instance == null || ECSLoadBridge.Instance.IsLoaded;

        /// <summary>
        /// Loads once the save is final (the cloud check can replace SaveManager.Current). Loading at scene
        /// start read the save about to be replaced, and the next flush wrote that stale state back: the same
        /// bug AchievementService had (2026-11-17).
        /// </summary>
        System.Collections.IEnumerator EnsureTodayWhenSaveIsFinal()
        {
            float waited = 0f;
            while (!IsSaveFinal && waited < SaveWaitTimeoutSeconds)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!IsSaveFinal)
                GameLogger.Warning($"[DailyEvents] Save still NOT final after {waited:0.0}s; loading anyway.");
            ReloadFromSave();
            EnsureToday();
        }

        /// <summary>On resume, a new UTC day may have started while backgrounded; re-sync and check.</summary>
        void OnApplicationPause(bool paused)
        {
            if (paused || !IsSaveFinal) return;
            ServerClock.Invalidate();
            _lastSyncFailedAt = float.NegativeInfinity;
            EnsureToday();
        }

        /// <summary>
        /// Ensures content is loaded, in-memory state is current, and today's challenge set is rolled.
        /// Safe to call repeatedly (e.g. each time the panel opens) — only re-rolls when expired, empty,
        /// or stale. Call this from the UI rather than relying solely on Start(), so the panel survives
        /// init-order races where SaveManager was not yet ready when this service's Start() ran.
        /// </summary>
        public void EnsureToday()
        {
            // Remote kill-switch: skip rolling challenges / advancing the calendar when disabled.
            // The HUD also hides the Daily nav button, so the panel is unreachable in this state.
            if (!FeatureFlags.DailyEventsEnabled) return;

            EnsureContent();
            if (_todaysChallengeIds.Count == 0)
                ReloadFromSave();

            var now = ServerClock.UtcNow;
            if (now.HasValue) { CheckDailyReset(now.Value); return; }

            if (_syncing || Time.realtimeSinceStartup - _lastSyncFailedAt < SyncRetrySeconds) return;
            _syncing = true;
            ServerClock.StartSync(this, ok =>
            {
                _syncing = false;
                var synced = ServerClock.UtcNow;
                if (ok && synced.HasValue)
                {
                    CheckDailyReset(synced.Value);
                }
                else
                {
                    _lastSyncFailedAt = Time.realtimeSinceStartup;
                    GameLogger.Warning("[DailyEvents] No server time (offline?): challenges keep their current set " +
                                       "and the login reward waits until a sync succeeds.");
                    MarkDayChecked();
                }
                OnChanged?.Invoke();   // the open panel switches between "connect" and claimable
            });
        }

        /// <summary>Replays challenge events held until today's set was known. Runs once per session.</summary>
        void MarkDayChecked()
        {
            if (_dayChecked) return;
            _dayChecked = true;
            if (_pendingEvents.Count == 0) return;
            GameLogger.Info($"[DailyEvents] Replaying {_pendingEvents.Count} challenge event(s) held until today's set was checked.");
            var pending = new List<(AchievementTrigger, int)>(_pendingEvents);
            _pendingEvents.Clear();
            foreach (var (trigger, delta) in pending)
                Accumulate(trigger, delta);
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
        /// <paramref name="now"/> must be trusted (server) time; EnsureToday supplies it.
        /// </summary>
        public void CheckDailyReset(DateTime now)
        {
            try { CheckDailyResetCore(now); }
            finally { MarkDayChecked(); }
        }

        void CheckDailyResetCore(DateTime now)
        {
            var save = SaveManager.Instance?.Current;

            // Without a save (e.g. GameScene entered without the Splash bootstrap that creates
            // SaveManager) we cannot read or persist the reset timestamp, but we still roll a set
            // into memory so the panel displays today's challenges. MaxValue means time-expiry alone
            // never forces a re-roll in that case — we only roll when the set is empty or stale.
            DateTime nextReset = save != null ? ParseOrEpoch(save.dailyChallengeResetUtc) : DateTime.MaxValue;

            // Re-roll when expired, empty, or stale (stored ids no longer exist in the current pool,
            // e.g. the content asset changed since the set was saved).
            bool stale     = _todaysChallengeIds.Count > 0 && _todaysChallengeIds.Any(id => GetChallenge(id) == null);
            bool needsRoll = now >= nextReset || _todaysChallengeIds.Count == 0 || stale;
            if (!needsRoll)
            {
                // A deadline past tomorrow's midnight was written from a clock running ahead; pull it back so
                // the set rolls on the real day boundary instead of staying stuck until the fake date.
                if (save != null && nextReset > NextMidnightUtc(now))
                {
                    GameLogger.Warning($"[DailyEvents] Challenge deadline {nextReset:o} is past today's end " +
                                       $"{NextMidnightUtc(now):o} (written from a fast clock); clamping, no re-roll.");
                    save.dailyChallengeResetUtc = NextMidnightUtc(now).ToString("o");
                }
                return;
            }

            string why = _todaysChallengeIds.Count == 0 ? "no set" : stale ? "stale ids" : $"deadline {nextReset:o} passed";
            GameLogger.Info($"[DailyEvents] Rolling challenges ({why}); server now {now:o}, device now {DateTime.UtcNow:o}.");
            RollChallenges(now);

            if (save != null)
            {
                save.dailyChallengeResetUtc = NextMidnightUtc(now).ToString("o");
                FlushToSave();
            }
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
            if (delta <= 0) return;
            if (!_dayChecked)
            {
                _pendingEvents.Add((trigger, delta));
                return;
            }
            if (content == null || _todaysChallengeIds.Count == 0) return;

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

        /// <summary>False while no server time is available (see <see cref="WaitingForServerTime"/>).</summary>
        public bool CanClaimLoginReward()
        {
            var now = ServerClock.UtcNow;
            return now.HasValue && CanClaimLoginReward(now.Value);
        }

        /// <summary>True if today's (UTC) login reward has not been claimed yet.</summary>
        public bool CanClaimLoginReward(DateTime now)
        {
            var save = SaveManager.Instance?.Current;
            if (save == null) return false;
            if (content?.loginRewards == null || content.loginRewards.Length == 0) return false;

            var last = ParseOrEpoch(save.lastLoginRewardUtc);
            if (last.Date > now.Date && !_warnedFutureLoginClaim)
            {
                _warnedFutureLoginClaim = true;
                GameLogger.Warning($"[DailyEvents] Last login reward is dated {last:yyyy-MM-dd}, after today " +
                                   $"{now:yyyy-MM-dd} (claimed with the phone's date set ahead); no claim until then.");
            }
            // Compare dates, not timestamps, to avoid double-grant within the same UTC day.
            return last == DateTime.MinValue || last.Date < now.Date;
        }

        public bool ClaimLoginReward()
        {
            var now = ServerClock.UtcNow;
            if (now.HasValue) return ClaimLoginReward(now.Value);
            GameLogger.Warning("[DailyEvents] Login reward claim refused: no server time yet.");
            EnsureToday();   // starts a sync if the retry window allows
            return false;
        }

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
            _pendingEvents.Clear();
            _dayChecked = false;
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

        static DateTime ParseOrEpoch(string iso)
        {
            if (string.IsNullOrEmpty(iso) ||
                !DateTime.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture,
                                   System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                return DateTime.MinValue;
            return dt.Kind == DateTimeKind.Local ? dt.ToUniversalTime() : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        }

        static DateTime NextMidnightUtc(DateTime from) =>
            from.Date.AddDays(1); // 00:00 UTC tomorrow
    }
}
