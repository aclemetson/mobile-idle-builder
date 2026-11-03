using System;
using System.Globalization;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Decides whether the next sign-in attempt must be interactive (account picker shown)
    /// or may proceed silently (cached session reused).
    ///
    /// Rules:
    ///   • No full-auth record on device  → interactive  (first install or cleared data)
    ///   • Last full auth ≥ 90 days ago   → interactive  (mandatory periodic re-verification)
    ///   • Last app open ≥ 30 days ago    → interactive  (long inactivity)
    ///   • Otherwise                      → silent OK
    ///
    /// Timestamps are stored in PlayerPrefs (device-local, not cloud-synced).
    /// </summary>
    public static class AuthSessionPolicy
    {
        public const int InactivityDays      = 30;
        public const int FullAuthIntervalDays = 90;

        public const string k_LastOpenedKey  = "auth_lastOpenedUtc";
        public const string k_LastFullAuthKey = "auth_lastFullAuthUtc";

        /// <summary>
        /// Returns true when the caller must show the Google account picker instead of
        /// attempting a silent sign-in.
        /// </summary>
        public static bool RequiresInteractiveSignIn()
        {
            var now = DateTime.UtcNow;

            // 90-day mandatory re-verification (also covers first install)
            string lastFullAuthStr = PlayerPrefs.GetString(k_LastFullAuthKey, "");
            if (string.IsNullOrEmpty(lastFullAuthStr))
                return true;

            var lastFullAuth = DateTime.Parse(lastFullAuthStr, null, DateTimeStyles.RoundtripKind);
            if ((now - lastFullAuth).TotalDays >= FullAuthIntervalDays)
                return true;

            // 30-day inactivity check
            string lastOpenedStr = PlayerPrefs.GetString(k_LastOpenedKey, "");
            if (string.IsNullOrEmpty(lastOpenedStr))
                return true;

            var lastOpened = DateTime.Parse(lastOpenedStr, null, DateTimeStyles.RoundtripKind);
            if ((now - lastOpened).TotalDays >= InactivityDays)
                return true;

            return false;
        }

        /// <summary>Call once per app open (e.g. SaveManager.Awake) to keep the inactivity clock current.</summary>
        public static void RecordAppOpen()
        {
            PlayerPrefs.SetString(k_LastOpenedKey, DateTime.UtcNow.ToString("O"));
            PlayerPrefs.Save();
        }

        /// <summary>Call after a successful interactive (non-silent) Google Sign-In.</summary>
        public static void RecordFullAuth()
        {
            PlayerPrefs.SetString(k_LastFullAuthKey, DateTime.UtcNow.ToString("O"));
            PlayerPrefs.Save();
        }
    }
}
