using System;
using System.Globalization;

namespace MobileIdleBuilder
{
    /// <summary>
    /// When to show the notification opt-in card ("Stay in the loop?"). Pure logic over
    /// <see cref="SettingsData"/> so it can be unit tested.
    ///
    /// - Never asked: ask at the next startup (once the tutorial is over).
    /// - Answered either way: ask again on the first startup after <see cref="ReaskInterval"/> (30 days).
    /// - "Don't ask again" ticked: never ask again (the Settings toggle still works).
    /// - Already on (OS permission granted and the Settings toggle on): nothing to ask.
    /// </summary>
    public static class NotificationAskPolicy
    {
        public static readonly TimeSpan ReaskInterval = TimeSpan.FromDays(30);

        public static bool ShouldAsk(SettingsData s, NotificationPermission permission, DateTime nowUtc)
        {
            if (s == null || s.notificationNeverAsk) return false;
            if (permission == NotificationPermission.Granted && s.notificationsEnabled) return false;
            if (!s.notificationPromptShown) return true;
            return nowUtc >= NextAskUtc(s);
        }

        /// <summary>Next time the card may show; <see cref="DateTime.MinValue"/> when unknown (ask now).</summary>
        public static DateTime NextAskUtc(SettingsData s)
        {
            if (s == null || string.IsNullOrEmpty(s.notificationNextAskUtc)) return DateTime.MinValue;
            return DateTime.TryParse(s.notificationNextAskUtc, CultureInfo.InvariantCulture,
                                     DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t)
                ? t
                : DateTime.MinValue;
        }

        public static void RecordAsked(SettingsData s, DateTime nowUtc, bool neverAskAgain)
        {
            if (s == null) return;
            s.notificationPromptShown = true;
            s.notificationNextAskUtc  = nowUtc.Add(ReaskInterval).ToString("O", CultureInfo.InvariantCulture);
            if (neverAskAgain) s.notificationNeverAsk = true;
        }

        /// <summary>
        /// Whether to show the Android "on-time reminders" card (exact alarms, "Alarms &amp; reminders" special access).
        /// Once ever, either answer: only while notifications are on (permission granted and the toggle on), and only
        /// where the OS needs the grant (Android 12+ without it; Android 14+ denies it to new installs by default).
        /// </summary>
        public static bool ShouldAskExact(SettingsData s, NotificationPermission permission, bool exactNeedsGrant)
            => s != null && exactNeedsGrant && !s.exactAlarmAsked
               && permission == NotificationPermission.Granted && s.notificationsEnabled;

        public static void Reset(SettingsData s)
        {
            if (s == null) return;
            s.notificationPromptShown = false;
            s.notificationNextAskUtc  = "";
            s.notificationNeverAsk    = false;
            s.exactAlarmAsked         = false;
        }
    }
}
