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
    /// OS permission is granted. Permission is asked behind an in-game card (<see cref="ConfirmPrompt"/>), so the
    /// OS prompt never comes out of nowhere: at startup once the tutorial is over, then again every 30 days
    /// until the player turns notifications on or ticks "Don't ask again" (<see cref="NotificationAskPolicy"/>).
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

        /// <summary>Seconds after the game is playable (loaded, tutorial over) before the opt-in card shows.</summary>
        const float PromptDelaySeconds = 8f;
        const float PromptPollSeconds  = 2f;

        INotificationPlatform _platform;
        float _readySince = -1f;
        float _nextPromptCheck;
        bool  _promptOpen;
        bool  _promptHandledThisLaunch;
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
                hasClaimable: achievements != null ? new Func<AchievementCategory, bool>(achievements.HasClaimable) : null,
                surgeOfferEnabled: FeatureFlags.AdsEnabled);
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
            TelemetryService.Instance?.RecordNotificationPreference(on);
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
                    AskExactSoon();
                    break;
                case NotificationPermission.NotDetermined:
                    RequestPermission("settings", granted =>
                    {
                        onResult?.Invoke(granted && IsActive);
                        if (granted) AskExactSoon();
                    });
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

        // ── Dev console (testing on device) ─────────────────────────────────

        /// <summary>One-line state dump for the dev console.</summary>
        public string DevStatus()
        {
            var s = SettingsService.Instance?.Current;
            return $"flag={FeatureFlags.NotificationsEnabled} toggle={s?.notificationsEnabled} " +
                   $"permission={Permission} active={IsActive} asked={s?.notificationPromptShown} " +
                   $"nextAsk={(string.IsNullOrEmpty(s?.notificationNextAskUtc) ? "-" : s.notificationNextAskUtc)} " +
                   $"neverAsk={s?.notificationNeverAsk} " +
                   $"exactNeedsGrant={ExactNeedsGrant} exactAsked={s?.exactAlarmAsked} " +
                   $"platform={_platform?.GetType().Name}";
        }

        /// <summary>The plan the next background would schedule, without scheduling it.</summary>
        public string DevPreviewPlan()
        {
            var save = SaveManager.Instance?.Current;
            if (save == null) return "No save loaded.";
            var plan = NotificationPlanner.Plan(BuildInputs(save));
            if (plan.Count == 0) return "Nothing to schedule right now.";
            var sb = new System.Text.StringBuilder();
            foreach (var n in plan)
                sb.AppendLine($"  {n.Kind,-18} in {FormatIn(n.FireUtc - DateTime.UtcNow)} (local {n.FireUtc.ToLocalTime():ddd HH:mm})");
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Schedules one test notification <paramref name="seconds"/> from now, bypassing the planner (no quiet
        /// hours, no cap). Background the app before it fires: coming back cancels everything pending.
        /// </summary>
        public string DevScheduleTest(int seconds)
        {
            if (_platform == null) return "No platform.";
            if (Permission != NotificationPermission.Granted)
                return $"Permission is {Permission}. Run 'notif permission' first.";
            var (title, body) = NotificationTexts.For(NotificationKind.DevTest);
            _platform.Schedule(new PlannedNotification(NotificationKind.DevTest, NotificationChannel.Production,
                title, body, DateTime.UtcNow.AddSeconds(seconds)));
            return $"Test notification in {seconds}s. Background the app now (returning cancels it).";
        }

        /// <summary>Clears the "already asked" mark and shows the opt-in card right away.</summary>
        public string DevShowPrompt()
        {
            if (!ConfirmPrompt.IsReady) return "HUD not ready.";
            SettingsService.Instance?.ResetNotificationAsk();
            ShowPrompt();
            return "Opt-in card shown.";
        }

        static string FormatIn(TimeSpan t) =>
            t.TotalHours >= 1 ? $"{(int)t.TotalHours}h{t.Minutes:00}m" : $"{(int)Math.Max(0, t.TotalMinutes)}m{Math.Max(0, t.Seconds):00}s";

        // ── Post-tutorial opt-in card ───────────────────────────────────────

        // Checked once per launch: shown at most once per app start, when NotificationAskPolicy says so
        // (never asked, or 30 days since the last answer, and not "Don't ask again").
        void TickPrompt()
        {
            if (_promptHandledThisLaunch || _promptOpen || Time.unscaledTime < _nextPromptCheck) return;
            _nextPromptCheck = Time.unscaledTime + PromptPollSeconds;

            if (!FeatureFlags.NotificationsEnabled) return;
            var settings = SettingsService.Instance?.Current;
            if (settings == null) return;
            bool askOptIn = NotificationAskPolicy.ShouldAsk(settings, Permission, DateTime.UtcNow);
            // Players who turned notifications on before the on-time card existed get it once at startup.
            bool askExact = !askOptIn && NotificationAskPolicy.ShouldAskExact(settings, Permission, ExactNeedsGrant);
            if (!askOptIn && !askExact)
            {
                _promptHandledThisLaunch = true;   // not due at startup: wait for the next launch
                return;
            }

            // Wait until the game is playable: loaded, tutorial over (a new player is asked right after it),
            // and nothing else on screen.
            var save = SaveManager.Instance?.Current;
            bool ready = save?.tutorial != null && !save.tutorial.isActive
                         && ECSLoadBridge.Instance != null && ECSLoadBridge.Instance.IsLoaded;
            if (!ready) { _readySince = -1f; return; }
            if (_readySince < 0f) _readySince = Time.unscaledTime;
            if (Time.unscaledTime - _readySince < PromptDelaySeconds) return;

            if (!ConfirmPrompt.IsReady || ConfirmPrompt.IsOpen || SaveConflictPrompt.IsOpen || PlayerNamePromptController.IsOpen) return;
            if (IsDialoguePlaying()) return;

            if (askOptIn) ShowPrompt();
            else
            {
                _promptHandledThisLaunch = true;
                ShowExactPrompt("startup");
            }
        }

        void ShowPrompt()
        {
            _promptOpen = true;
            _promptHandledThisLaunch = true;
            ConfirmPrompt.Show(NotificationTexts.PromptTitle, NotificationTexts.PromptMessage,
                NotificationTexts.PromptConfirm,
                onConfirm: () =>
                {
                    _promptOpen = false;
                    SettingsService.Instance?.RecordNotificationAsk(DateTime.UtcNow, ConfirmPrompt.CheckboxChecked);
                    SettingsService.Instance?.SetNotifications(true);
                    switch (Permission)
                    {
                        case NotificationPermission.Granted:
                            AskExactSoon();   // the toggle was off, the OS already allowed it
                            break;
                        case NotificationPermission.NotDetermined:
                            RequestPermission("startup_prompt", granted => { if (granted) AskExactSoon(); });
                            break;
                        case NotificationPermission.Denied:
                            // The OS will not show its prompt again: send the player to the app's OS settings.
                            _platform?.OpenSettings();
                            break;
                    }
                },
                onCancel: () =>
                {
                    _promptOpen = false;
                    // "Not now": no OS prompt, so the OS one-time ask is kept for a later "Notify me".
                    SettingsService.Instance?.RecordNotificationAsk(DateTime.UtcNow, ConfirmPrompt.CheckboxChecked);
                    TelemetryService.Instance?.RecordNotificationPermission(
                        ConfirmPrompt.CheckboxChecked ? "startup_prompt_never" : "startup_prompt_declined", false);
                },
                cancelLabel: NotificationTexts.PromptCancel,
                destructive: false,
                checkboxLabel: NotificationTexts.PromptNeverAsk);
        }

        // ── On-time reminders (Android exact alarms) ────────────────────────

        bool ExactNeedsGrant
        {
            get
            {
                try { return _platform != null && _platform.ExactAlarmsNeedGrant; }
                catch (Exception e)
                {
                    GameLogger.Warning($"[Notifications] Exact alarm check failed: {e.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Right after the player allows notifications: offer "Alarms &amp; reminders" once, where the OS needs it.
        /// A short wait lets the OS permission dialog finish closing first. If another card is open by then,
        /// the next startup asks instead (<see cref="TickPrompt"/>).
        /// </summary>
        void AskExactSoon() => StartCoroutine(AskExactRoutine());

        IEnumerator AskExactRoutine()
        {
            yield return new WaitForSecondsRealtime(0.5f);
            var settings = SettingsService.Instance?.Current;
            if (!NotificationAskPolicy.ShouldAskExact(settings, Permission, ExactNeedsGrant)) yield break;
            if (!ConfirmPrompt.IsReady || ConfirmPrompt.IsOpen || SaveConflictPrompt.IsOpen || PlayerNamePromptController.IsOpen) yield break;
            ShowExactPrompt("after_opt_in");
        }

        void ShowExactPrompt(string source)
        {
            // ConfirmPrompt confirms at once when the HUD is not up: never open OS settings unasked.
            if (!ConfirmPrompt.IsReady) return;
            _promptOpen = true;
            ConfirmPrompt.Show(NotificationTexts.ExactTitle, NotificationTexts.ExactMessage,
                NotificationTexts.ExactConfirm,
                onConfirm: () =>
                {
                    _promptOpen = false;
                    SettingsService.Instance?.MarkExactAlarmAsked();
                    TelemetryService.Instance?.RecordNotificationPermission($"exact_{source}", true);
                    try { _platform?.RequestExactAlarms(); }
                    catch (Exception e) { GameLogger.Warning($"[Notifications] Could not open Alarms & reminders: {e.Message}"); }
                },
                onCancel: () =>
                {
                    _promptOpen = false;
                    SettingsService.Instance?.MarkExactAlarmAsked();
                    TelemetryService.Instance?.RecordNotificationPermission($"exact_{source}_declined", false);
                },
                cancelLabel: NotificationTexts.ExactCancel,
                destructive: false);
        }

        /// <summary>Dev console: shows the on-time reminders card now, whatever was answered before.</summary>
        public string DevShowExactPrompt()
        {
            if (!ConfirmPrompt.IsReady) return "HUD not ready.";
            ShowExactPrompt("dev");
            return $"On-time card shown (exactNeedsGrant={ExactNeedsGrant}).";
        }

        static bool IsDialoguePlaying()
        {
            var dialogue = FindAnyObjectByType<DialogueController>();
            return dialogue != null && dialogue.IsPlaying;
        }
    }
}
