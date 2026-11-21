using System;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Singleton retention service: the 28-day login reward calendar. It is shown at the top of the Daily tab
    /// of the Achievements menu. Daily tasks are Daily achievements (<see cref="AchievementService"/>); the
    /// separate rotating challenge pool was merged into them on 2026-11-20 (it duplicated four of them).
    ///
    /// Time logic is UTC throughout and uses SERVER time (ServerClock), like AchievementService: moving the
    /// phone's date must not re-open the login reward. With no server time (offline) the reward cannot be
    /// claimed until a sync succeeds. The calendar advances only when the player CLAIMS (kindness rule: a
    /// missed day never resets the streak; the index simply does not advance). It loops after day 28.
    ///
    /// Reward grants reuse existing infrastructure: crystals → SaveData.paidCurrency,
    /// entropy → ECSLoadBridge.AddEntropy, prestige currency → ECSLoadBridge.AddPrestigeCurrency.
    /// State survives prestige. Core logic methods take an injected <c>now</c> so EditMode tests can drive them.
    /// </summary>
    public class DailyEventService : SingletonMonoBehaviour<DailyEventService>
    {
        protected override bool PersistAcrossScenes => false;

        [SerializeField] DailyContentSO content;

        /// <summary>Fired whenever the login reward state changes so the Daily tab can refresh.</summary>
        public event Action OnChanged;

        public DailyContentSO Content => content;

        // Server-time sync: one at a time; after a failure, retries wait SyncRetrySeconds.
        bool  _syncing;
        float _lastSyncFailedAt = float.NegativeInfinity;
        const float SyncRetrySeconds = 30f;
        bool  _warnedFutureLoginClaim;

        /// <summary>True while no server time is available; the tab says to connect instead of "claimed".</summary>
        public bool WaitingForServerTime => !ServerClock.IsSynced;

        void Start() => EnsureToday();

        /// <summary>On resume, a new UTC day may have started while backgrounded; re-sync.</summary>
        void OnApplicationPause(bool paused)
        {
            if (paused) return;
            ServerClock.Invalidate();
            _lastSyncFailedAt = float.NegativeInfinity;
            EnsureToday();
        }

        /// <summary>
        /// Loads content and makes sure server time is available, so the login reward can be claimed. Safe to
        /// call repeatedly (the Daily tab calls it each time it is drawn).
        /// </summary>
        public void EnsureToday()
        {
            // Remote kill-switch: the login calendar is hidden and unclaimable when disabled.
            if (!FeatureFlags.DailyEventsEnabled) return;

            EnsureContent();
            if (ServerClock.UtcNow.HasValue) return;

            if (_syncing || Time.realtimeSinceStartup - _lastSyncFailedAt < SyncRetrySeconds) return;
            _syncing = true;
            ServerClock.StartSync(this, ok =>
            {
                _syncing = false;
                if (!ok || !ServerClock.UtcNow.HasValue)
                {
                    _lastSyncFailedAt = Time.realtimeSinceStartup;
                    GameLogger.Warning("[DailyEvents] No server time (offline?): the login reward waits until a sync succeeds.");
                }
                OnChanged?.Invoke();   // the open tab switches between "connect" and claimable
            });
        }

        void EnsureContent()
        {
            if (content == null)
                content = Resources.Load<DailyContentSO>("DailyContent");
            if (content == null)
                GameLogger.Warning("[DailyEvents] DailyContent asset not found in Resources — login calendar disabled.");
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

        // ── Dev / lifecycle helpers ───────────────────────────────────────────

        /// <summary>Clears in-memory state after a dev "clear save" so it matches the reset SaveData.</summary>
        public void ResetInMemory()
        {
            _warnedFutureLoginClaim = false;
            GameLogger.Debug("[DailyEvents] In-memory state cleared by ResetInMemory().");
        }

        // ── Internals ─────────────────────────────────────────────────────────

        static DateTime ParseOrEpoch(string iso)
        {
            if (string.IsNullOrEmpty(iso) ||
                !DateTime.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture,
                                   System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                return DateTime.MinValue;
            return dt.Kind == DateTimeKind.Local ? dt.ToUniversalTime() : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        }
    }
}
