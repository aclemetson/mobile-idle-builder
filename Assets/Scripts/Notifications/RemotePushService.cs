using System;
using System.Collections.Generic;
using Unity.Services.Core;
using Unity.Services.PushNotifications;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Remote push campaigns sent from the Unity Cloud dashboard (UGS Push Notifications, FCM on Android and
    /// APNs on iOS). This only registers the device; the campaigns themselves (who, what, when) live in the
    /// dashboard. See docs/runbooks/push-campaigns.md.
    ///
    /// Registers once per launch when all of these hold:
    /// - the push.enabled flag is on
    /// - analytics data collection is running (analytics.enabled): UGS ties the push token to the analytics
    ///   player, so without collection the dashboard cannot target the device
    /// - the Settings toggle is on and the OS permission is already granted (asked by
    ///   <see cref="NotificationService"/>, so the player only ever sees one prompt)
    ///
    /// A campaign that arrives while the game is open shows as the in-game banner. One tapped from the tray
    /// opens the game, and the SDK records <c>notificationOpened</c> itself.
    /// Self-bootstrapped: no scene placement needed.
    /// </summary>
    public class RemotePushService : SingletonMonoBehaviour<RemotePushService>
    {
        protected override bool PersistAcrossScenes => true;

        const float CheckIntervalSeconds = 3f;

        bool  _subscribed;
        bool  _registering;
        bool  _registered;
        float _nextCheck;

        /// <summary>The device push token once registered (logged for dashboard "Test on Device").</summary>
        public string Token { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            new GameObject(nameof(RemotePushService)).AddComponent<RemotePushService>();
        }

        void Update()
        {
            if (_registered || _registering || Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + CheckIntervalSeconds;
            if (!ReadyToRegister(out _)) return;
            Register();
        }

        /// <summary>Whether registration may run now; <paramref name="reason"/> says why not (dev console).</summary>
        public bool ReadyToRegister(out string reason)
        {
            reason = null;
            if (!FeatureFlags.PushEnabled)                                  reason = "push.enabled flag is off";
            else if (UnityServices.State != ServicesInitializationState.Initialized) reason = "UGS not initialized";
            else if (TelemetryService.Instance == null || !TelemetryService.Instance.IsCollecting)
                                                                             reason = "analytics not collecting (analytics.enabled off)";
            else if (!(SettingsService.Instance?.Current?.notificationsEnabled ?? false))
                                                                             reason = "Settings toggle off";
            else if (NotificationService.Instance == null
                     || NotificationService.Instance.Permission != NotificationPermission.Granted)
                                                                             reason = "OS permission not granted";
            return reason == null;
        }

        async void Register()
        {
            _registering = true;
            try
            {
                var push = PushNotificationsService.Instance;
                // Must subscribe before registering: the SDK refuses new subscribers once it has a token.
                if (!_subscribed)
                {
                    push.OnRemoteNotificationReceived += OnRemoteNotification;
                    _subscribed = true;
                }

                Token = await push.RegisterForPushNotificationsAsync();
                _registered = true;
                GameLogger.Info($"[Push] Registered for remote notifications. Token: {Token}");
            }
            catch (Exception e)
            {
                // Denied, no Google Play services, no network... Local notifications are unaffected.
                // Not retried this launch so a failing device does not spin.
                _registered = true;
                GameLogger.Warning($"[Push] Registration failed: {e.Message}");
            }
            finally
            {
                _registering = false;
            }
        }

        void OnRemoteNotification(Dictionary<string, object> data)
        {
            if (data == null) return;
            string title = Read(data, "title");
            string body  = Read(data, "alert");
            GameLogger.Info($"[Push] Received: '{title}' / '{body}'");

            string message = string.IsNullOrEmpty(title) ? body
                           : string.IsNullOrEmpty(body)  ? title
                           : $"{title}: {body}";
            if (string.IsNullOrEmpty(message)) return;
            FindAnyObjectByType<HUDController>()?.ShowNotification("✉", message);
        }

        static string Read(Dictionary<string, object> data, string key) =>
            data.TryGetValue(key, out var v) && v != null ? v.ToString() : null;

        /// <summary>One-line state for the dev console.</summary>
        public string DevStatus()
        {
            ReadyToRegister(out var reason);
            return $"registered={_registered && !string.IsNullOrEmpty(Token)} registering={_registering} " +
                   $"ready={reason == null}{(reason != null ? $" ({reason})" : "")} " +
                   $"token={(string.IsNullOrEmpty(Token) ? "-" : Token)}";
        }
    }
}
