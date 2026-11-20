using System;
using System.Collections;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using Unity.Notifications.Android;
#elif UNITY_IOS && !UNITY_EDITOR
using Unity.Notifications.iOS;
#endif

namespace MobileIdleBuilder
{
    public enum NotificationPermission
    {
        NotDetermined,   // never asked (Android 13+ / iOS); the OS will show its prompt
        Granted,
        Denied,          // the player said no: only the OS settings page can turn it back on
    }

    /// <summary>The OS side of local notifications. One implementation per platform, chosen by <see cref="Create"/>.</summary>
    public interface INotificationPlatform
    {
        void Initialize();
        NotificationPermission Permission { get; }
        /// <summary>Coroutine: shows the OS prompt if needed, then reports whether notifications are allowed.</summary>
        IEnumerator RequestPermission(Action<bool> done);
        void Schedule(PlannedNotification n);
        /// <summary>Removes every scheduled and delivered notification and clears the app badge.</summary>
        void CancelAll();
        void OpenSettings();
        /// <summary>The kind of the notification the player tapped to open the app, if any.</summary>
        NotificationKind? ConsumeLaunchNotification();
    }

    public static class NotificationPlatform
    {
        public static string ChannelId(NotificationChannel c) => "qf_" + c.ToString().ToLowerInvariant();

        public static INotificationPlatform Create()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidNotificationPlatform();
#elif UNITY_IOS && !UNITY_EDITOR
            return new IOSNotificationPlatform();
#else
            return new EditorNotificationPlatform();
#endif
        }

        internal static NotificationKind? KindFromId(int id)
        {
            int k = id - 1000;
            return Enum.IsDefined(typeof(NotificationKind), k) ? (NotificationKind)k : (NotificationKind?)null;
        }
    }

    /// <summary>Editor and desktop: nothing reaches an OS, the plan is logged so it can be checked by eye.</summary>
    public class EditorNotificationPlatform : INotificationPlatform
    {
        // Editor stand-in for the OS answer; starts unasked like a fresh install.
        static NotificationPermission s_permission = NotificationPermission.NotDetermined;

        public void Initialize() { }
        public NotificationPermission Permission => s_permission;

        public IEnumerator RequestPermission(Action<bool> done)
        {
            if (s_permission == NotificationPermission.NotDetermined) s_permission = NotificationPermission.Granted;
            GameLogger.Info($"[Notifications] (editor) permission request -> {s_permission}");
            done?.Invoke(s_permission == NotificationPermission.Granted);
            yield break;
        }

        public void Schedule(PlannedNotification n) =>
            GameLogger.Info($"[Notifications] (editor) would schedule {n} (local {n.FireUtc.ToLocalTime():ddd HH:mm})");

        public void CancelAll() => GameLogger.Debug("[Notifications] (editor) cancel all");
        public void OpenSettings() => GameLogger.Info("[Notifications] (editor) would open OS notification settings");
        public NotificationKind? ConsumeLaunchNotification() => null;
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    public class AndroidNotificationPlatform : INotificationPlatform
    {
        string _lastLaunchKey;

        public void Initialize()
        {
            AndroidNotificationCenter.Initialize();
            foreach (NotificationChannel c in Enum.GetValues(typeof(NotificationChannel)))
            {
                AndroidNotificationCenter.RegisterNotificationChannel(new AndroidNotificationChannel
                {
                    Id          = NotificationPlatform.ChannelId(c),
                    Name        = NotificationTexts.ChannelName(c),
                    Description = NotificationTexts.ChannelDescription(c),
                    Importance  = c == NotificationChannel.Reminders ? Importance.Low : Importance.Default,
                    CanShowBadge = true,
                });
            }
        }

        public NotificationPermission Permission => AndroidNotificationCenter.UserPermissionToPost switch
        {
            PermissionStatus.Allowed       => NotificationPermission.Granted,
            PermissionStatus.NotRequested  => NotificationPermission.NotDetermined,
            _                              => NotificationPermission.Denied,
        };

        public IEnumerator RequestPermission(Action<bool> done)
        {
            if (AndroidNotificationCenter.UserPermissionToPost == PermissionStatus.Allowed)
            {
                done?.Invoke(true);
                yield break;
            }
            var request = new PermissionRequest();
            while (request.Status == PermissionStatus.RequestPending) yield return null;
            done?.Invoke(request.Status == PermissionStatus.Allowed);
        }

        public void Schedule(PlannedNotification n)
        {
            var notification = new AndroidNotification
            {
                Title    = n.Title,
                Text     = n.Body,
                FireTime = n.FireUtc.ToLocalTime(),   // the package converts back to epoch ms via ToUniversalTime
                Group    = NotificationPlatform.ChannelId(n.Channel),
                IntentData = n.Kind.ToString(),
            };
            notification.SmallIcon = NotificationService.AndroidSmallIcon;
            AndroidNotificationCenter.SendNotificationWithExplicitID(notification,
                NotificationPlatform.ChannelId(n.Channel), n.Id);
        }

        public void CancelAll() => AndroidNotificationCenter.CancelAllNotifications();

        public void OpenSettings() => AndroidNotificationCenter.OpenNotificationSettings();

        public NotificationKind? ConsumeLaunchNotification()
        {
            var intent = AndroidNotificationCenter.GetLastNotificationIntent();
            if (intent == null) return null;
            // The same intent is returned until another notification is tapped: report each one once.
            string key = intent.Id + "@" + intent.Notification.FireTime.Ticks;
            if (key == _lastLaunchKey) return null;
            _lastLaunchKey = key;
            return NotificationPlatform.KindFromId(intent.Id);
        }
    }
#endif

#if UNITY_IOS && !UNITY_EDITOR
    public class IOSNotificationPlatform : INotificationPlatform
    {
        const AuthorizationOption Options = AuthorizationOption.Alert | AuthorizationOption.Badge | AuthorizationOption.Sound;
        string _lastLaunchId;

        public void Initialize() { }

        public NotificationPermission Permission =>
            iOSNotificationCenter.GetNotificationSettings().AuthorizationStatus switch
            {
                AuthorizationStatus.NotDetermined => NotificationPermission.NotDetermined,
                AuthorizationStatus.Denied        => NotificationPermission.Denied,
                _                                 => NotificationPermission.Granted,
            };

        public IEnumerator RequestPermission(Action<bool> done)
        {
            using var request = new AuthorizationRequest(Options, registerForRemoteNotifications: false);
            while (!request.IsFinished) yield return null;
            done?.Invoke(request.Granted);
        }

        public void Schedule(PlannedNotification n)
        {
            var interval = n.FireUtc - DateTime.UtcNow;
            if (interval < TimeSpan.FromSeconds(1)) return;
            iOSNotificationCenter.ScheduleNotification(new iOSNotification
            {
                Identifier       = n.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Title            = n.Title,
                Body             = n.Body,
                ThreadIdentifier = NotificationPlatform.ChannelId(n.Channel),
                Badge            = 1,
                ShowInForeground = false,
                Trigger          = new iOSNotificationTimeIntervalTrigger { TimeInterval = interval, Repeats = false },
            });
        }

        public void CancelAll()
        {
            iOSNotificationCenter.RemoveAllScheduledNotifications();
            iOSNotificationCenter.RemoveAllDeliveredNotifications();
            iOSNotificationCenter.ApplicationBadge = 0;
        }

        public void OpenSettings() => iOSNotificationCenter.OpenNotificationSettings();

        public NotificationKind? ConsumeLaunchNotification()
        {
            var n = iOSNotificationCenter.GetLastRespondedNotification();
            if (n == null || n.Identifier == _lastLaunchId) return null;
            _lastLaunchId = n.Identifier;
            return int.TryParse(n.Identifier, out int id) ? NotificationPlatform.KindFromId(id) : null;
        }
    }
#endif
}
