using System;
using System.Collections;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Local OS notifications (Android + iOS) for the timers the player waits on: idle storage full, research,
    /// crafting, Cyclotron, boosts, daily rewards and comeback reminders.
    ///
    /// Flow: every time the app goes to the background (after <see cref="SaveManager"/> writes the save) the
    /// pending notifications are rebuilt from the save by <see cref="NotificationPlanner"/>; every time it comes
    /// back, all of them are cancelled, so a notification never fires while the player is in the game and
    /// never describes a stale state.
    ///
    /// Runs only when the <c>notifications.enabled</c> flag is on, the player has the Settings toggle on and the
    /// OS permission is granted. Permission is first asked once the tutorial is over, behind an in-game card
    /// (<see cref="ConfirmPrompt"/>), so the OS prompt never comes out of nowhere.
    /// Self-bootstrapped: no scene placement needed.
    /// </summary>
    public class NotificationService : SingletonMonoBehaviour<NotificationService>
    {
        protected override bool PersistAcrossScenes => true;

        /// <summary>
        /// Android status-bar icon id, registered in Project Settings > Mobile Notifications
        /// (Assets/Art/Notifications/notif_icon_small.png, white on transparent as Android requires).
        /// </summary>
        public const string AndroidSmallIcon = "icon_small";

        /// <summary>Seconds after the tutorial ends before the opt-in card shows, so it does not land on the last dialogue.</summary>
        const float PromptDelaySeconds = 20f;
        const float PromptPollSeconds  = 2f;

        INotificationPlatform _platform;
        float _tutorialDoneSince = -1f;
        float _nextPromptCheck;
        bool  _promptOpen;
        bool  _subscribed;

        /// <summary>Test seam: replaces the OS layer.</summary>
        internal INotificationPlatform Platform { get => _platform; set => _platform = value; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            new GameObject(nameof(NotificationService)).AddComponent<NotificationService>();
        }

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;

            _platform = NotificationPlatform.Create();
            try { _platform.Initialize(); }
            catch (Exception e) { GameLogger.Error($"[Notifications] Platform init failed: {e.Message}"); }

            // Whatever was scheduled last time is out of date now that the player is here.
            ClearPending();
            ReportLaunchNotification();
        }

        void Update()
        {
            if (!_subscribed && SaveManager.Instance != null)
            {
                SaveManager.Instance.Backgrounding += OnBackgrounding;
                _subscribed = true;
            }
            TickPrompt();
        }

        protected override void OnDestroy()
        {
            if (_subscribed && SaveManager.Instance != null) SaveManager.Instance.Backgrounding -= OnBackgrounding;
            base.OnDestroy();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) return;   // scheduling happens on SaveManager.Backgrounding, after the save is written
            ClearPending();
            ReportLaunchNotification();
        }

        // ── State ───────────────────────────────────────────────────────────

        public NotificationPermission Permission => _platform?.Permission ?? NotificationPermission.Denied;

        /// <summary>Notifications will actually be scheduled: flag on, Settings toggle on, OS permission granted.</summary>
        public bool IsActive =>
            FeatureFlags.NotificationsEnabled
            && (SettingsService.Instance?.Current?.notificationsEnabled ?? false)
            && Permission == NotificationPermission.Granted;

        // ── Scheduling ──────────────────────────────────────────────────────

        void OnBackgrounding()
        {
            if (!IsActive) return;
            ScheduleFromSave();
        }

        /// <summary>Cancels everything pending, then schedules the plan for the current save.</summary>
        public void ScheduleFromSave()
        {
            var save = SaveManager.Instance?.Current;
            if (save == null || _platform == null) return;

            try
            {
                _platform.CancelAll();
                var plan = NotificationPlanner.Plan(BuildInputs(save));
                foreach (var n in plan) _platform.Schedule(n);
                GameLogger.Info($"[Notifications] Scheduled {plan.Count}: {string.Join(", ", plan)}");
            }
            catch (Exception e)
            {
                // Never let a notification problem break the save/pause path.
                GameLogger.Error($"[Notifications] Scheduling failed: {e}");
            }
        }

        static NotificationInputs BuildInputs(SaveData save)
        {
            var config = SaveManager.Instance?.GameConfig;
            float idleCap = config != null
                ? OfflineCollectionService.GetEffectiveIdleCap(config, PersistentUpgradeService.Instance)
                : 0f;

            var wallNow = DateTime.UtcNow;
            var achievements = AchievementService.Instance;
            return NotificationInputs.FromSave(
                save,
                wallNowUtc: wallNow,
                gameNow: GameClock.UtcNow,
                localOffset: TimeZoneInfo.Local.GetUtcOffset(wallNow),
                idleCapSeconds: idleCap,
                dailyRewardEnabled: FeatureFlags.DailyEventsEnabled && DailyEventService.Instance != null,
                hasClaimable: achievements != null ? new Func<AchievementCategory, bool>(achievements.HasClaimable) : null);
        }

        void ClearPending()
        {
            try { _platform?.CancelAll(); }
            catch (Exception e) { GameLogger.Warning($"[Notifications] Cancel failed: {e.Message}"); }
        }

        void ReportLaunchNotification()
        {
            NotificationKind? kind = null;
            try { kind = _platform?.ConsumeLaunchNotification(); }
            catch (Exception e) { GameLogger.Warning($"[Notifications] Launch check failed: {e.Message}"); }
            if (kind == null) return;
            GameLogger.Info($"[Notifications] Opened from notification {kind}");
            TelemetryService.Instance?.RecordNotificationOpened(kind.ToString());
        }

        // ── Settings toggle ─────────────────────────────────────────────────

        /// <summary>
        /// The Settings toggle. Turning it on asks for the OS permission when it was never asked, or opens the
        /// OS notification settings when the player denied it before (the OS will not ask twice).
        /// <paramref name="onResult"/> receives whether notifications are now active, for the toggle to show.
        /// </summary>
        public void SetEnabledFromSettings(bool on, Action<bool> onResult)
        {
            var settings = SettingsService.Instance;
            settings?.SetNotifications(on);
            if (!on)
            {
                ClearPending();
                onResult?.Invoke(false);
                return;
            }

            switch (Permission)
            {
                case NotificationPermission.Granted:
                    onResult?.Invoke(IsActive);
                    break;
                case NotificationPermission.NotDetermined:
                    RequestPermission("settings", granted => onResult?.Invoke(granted && IsActive));
                    break;
                default:
                    // Denied: only the OS can change it. Keep the preference on so it works once allowed.
                    _platform?.OpenSettings();
                    onResult?.Invoke(false);
                    break;
            }
        }

        /// <summary>Shows the OS permission prompt (if the OS still allows asking) and records the answer.</summary>
        public void RequestPermission(string source, Action<bool> done = null)
        {
            if (_platform == null) { done?.Invoke(false); return; }
            StartCoroutine(RequestRoutine(source, done));
        }

        IEnumerator RequestRoutine(string source, Action<bool> done)
        {
            bool granted = false;
            yield return _platform.RequestPermission(g => granted = g);
            GameLogger.Info($"[Notifications] Permission ({source}): {(granted ? "granted" : "denied")}");
            TelemetryService.Instance?.RecordNotificationPermission(source, granted);
            done?.Invoke(granted);
        }

        // ── Post-tutorial opt-in card ───────────────────────────────────────

        void TickPrompt()
        {
            if (_promptOpen || Time.unscaledTime < _nextPromptCheck) return;
            _nextPromptCheck = Time.unscaledTime + PromptPollSeconds;

            var settings = SettingsService.Instance?.Current;
            if (settings == null || settings.notificationPromptShown) return;
            if (!FeatureFlags.NotificationsEnabled) return;

            // Already decided at the OS level (granted earlier, or denied): nothing to ask.
            if (Permission != NotificationPermission.NotDetermined)
            {
                SettingsService.Instance.MarkNotificationPromptShown();
                return;
            }

            var save = SaveManager.Instance?.Current;
            bool tutorialDone = save?.tutorial != null && !save.tutorial.isActive
                                && ECSLoadBridge.Instance != null && ECSLoadBridge.Instance.IsLoaded;
            if (!tutorialDone) { _tutorialDoneSince = -1f; return; }
            if (_tutorialDoneSince < 0f) _tutorialDoneSince = Time.unscaledTime;
            if (Time.unscaledTime - _tutorialDoneSince < PromptDelaySeconds) return;

            if (!ConfirmPrompt.IsReady || ConfirmPrompt.IsOpen || SaveConflictPrompt.IsOpen) return;
            if (IsDialoguePlaying()) return;

            ShowPrompt();
        }

        void ShowPrompt()
        {
            _promptOpen = true;
            ConfirmPrompt.Show(NotificationTexts.PromptTitle, NotificationTexts.PromptMessage,
                NotificationTexts.PromptConfirm,
                onConfirm: () =>
                {
                    _promptOpen = false;
                    SettingsService.Instance?.MarkNotificationPromptShown();
                    SettingsService.Instance?.SetNotifications(true);
                    RequestPermission("post_tutorial");
                },
                onCancel: () =>
                {
                    _promptOpen = false;
                    // "Not now": no OS prompt, so the player can still turn it on from Settings later.
                    SettingsService.Instance?.MarkNotificationPromptShown();
                    TelemetryService.Instance?.RecordNotificationPermission("post_tutorial_declined", false);
                },
                cancelLabel: NotificationTexts.PromptCancel,
                destructive: false);
        }

        static bool IsDialoguePlaying()
        {
            var dialogue = FindAnyObjectByType<DialogueController>();
            return dialogue != null && dialogue.IsPlaying;
        }
    }
}
