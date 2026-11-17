using System;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>Which kind of account the cloud save is stored under.</summary>
    public enum CloudAccountKind
    {
        /// <summary>Not signed in (offline, or UGS failed to start).</summary>
        None,
        /// <summary>Anonymous UGS player: the save lives on this device's account only.</summary>
        Guest,
        /// <summary>Player linked to a Google account: the same save on every device signed in with it.</summary>
        Google,
        /// <summary>Editor username/password test account.</summary>
        Other,
    }

    /// <summary>How the Google sign-in bridge may ask for a Google credential.</summary>
    public enum GoogleAuthMode
    {
        /// <summary><see cref="AuthSessionPolicy"/> decides (account picker on first install, silent otherwise).</summary>
        Policy,
        /// <summary>Never show UI; fail if no cached Google credential.</summary>
        Silent,
        /// <summary>Always show the account picker (Settings "Sign in with Google").</summary>
        Interactive,
    }

    /// <summary>Result of attaching Google to the signed-in player.</summary>
    public enum GoogleSignInOutcome
    {
        Failed,
        /// <summary>The player already had Google; nothing changed.</summary>
        AlreadyGoogle,
        /// <summary>Google was attached to this (guest) player; same save, now reachable from other devices.</summary>
        Linked,
        /// <summary>The Google account already belonged to another player (another device's save); now signed in as that player.</summary>
        SwitchedAccount,
    }

    /// <summary>A Google ID token and the account's email (for display).</summary>
    public struct GoogleCredential
    {
        public string IdToken;
        public string Email;
    }

    /// <summary>What to do with the local save and the cloud save at startup or after an account switch.</summary>
    public enum SaveSyncAction { KeepLocal, TakeCloud, AskPlayer }

    /// <summary>
    /// Pure rules for reconciling this device's save with the cloud save (docs/agents/save-system.md,
    /// "Cross-device saves").
    ///
    /// Each device remembers, in PlayerPrefs, the last save it exchanged with the cloud (the sync marker):
    /// the UGS player id, that save's <c>lastSaved</c> stamp as stored in the cloud, and the content hash
    /// of the local save at that moment. Comparing both saves to the marker tells who changed since then:
    ///  - only the cloud changed (another device played): take the cloud save silently;
    ///  - only this device changed: keep it (the autosave pushes it);
    ///  - both changed, or the device just moved to another account: the saves have forked, ask the player.
    /// Without a marker (saves from before this existed) the newer stamp wins, as before.
    /// The local side compares content hashes, not stamps, because every autosave re-stamps the file.
    /// </summary>
    public static class SaveSync
    {
        /// <summary>PlayerPrefs key: "playerId|cloudStamp|localContentHash" of the last cloud exchange.</summary>
        public const string MarkerKey = "cloud_sync_marker";

        public struct Marker
        {
            public string PlayerId;
            public string CloudStamp;
            public ulong  LocalHash;
            public bool   IsSet => !string.IsNullOrEmpty(PlayerId) && !string.IsNullOrEmpty(CloudStamp);
        }

        /// <param name="localHash">Content hash of the local save (<see cref="LocalSaveService.ContentHash"/>).</param>
        /// <param name="accountSwitched">The device just moved from a guest player to an existing Google player.</param>
        public static SaveSyncAction Decide(SaveData local, ulong localHash, bool localIsNew, SaveData cloud,
                                            Marker marker, string playerId, bool accountSwitched)
        {
            if (cloud == null) return SaveSyncAction.KeepLocal;
            if (local == null || localIsNew) return SaveSyncAction.TakeCloud;

            bool otherAccount = marker.IsSet && marker.PlayerId != playerId;
            if (accountSwitched || otherAccount) return SaveSyncAction.AskPlayer;

            if (!marker.IsSet)
                return SaveManager.IsNewerStamp(cloud.lastSaved, local.lastSaved)
                    ? SaveSyncAction.TakeCloud : SaveSyncAction.KeepLocal;

            bool cloudChanged = CloudChangedSince(cloud.lastSaved, marker.CloudStamp);
            bool localChanged = localHash == 0 || localHash != marker.LocalHash;
            if (cloudChanged && localChanged) return SaveSyncAction.AskPlayer;
            return cloudChanged ? SaveSyncAction.TakeCloud : SaveSyncAction.KeepLocal;
        }

        /// <summary>
        /// Whether another device wrote the cloud save since the marker. The marker is written
        /// optimistically before a push (a push on backgrounding often finishes after the OS suspends the
        /// app), so when the app dies before that push lands the cloud still holds an OLDER save than the
        /// marker names. That is our own lost push, not another device's change: treating it as a change
        /// took the older cloud save over this device's progress on every launch, and with an unpushed
        /// autosave on top it asked "which save?" behind the loading screen (stuck at 99%). Another device
        /// stamps its save when it writes, so its save is newer than the marker.
        /// </summary>
        internal static bool CloudChangedSince(string cloudStamp, string markerStamp)
        {
            if (cloudStamp == markerStamp) return false;
            // Only when both stamps read as times; anything unreadable still counts as a change.
            if (SaveManager.TryParseStamp(cloudStamp, out var cloud) &&
                SaveManager.TryParseStamp(markerStamp, out var marker) &&
                cloud < marker)
                return false; // our push never landed
            return true;
        }

        public static string FormatMarker(string playerId, string cloudStamp, ulong localHash) =>
            $"{playerId}|{cloudStamp}|{localHash}";

        public static Marker ParseMarker(string text)
        {
            var m = new Marker();
            if (string.IsNullOrEmpty(text)) return m;
            var parts = text.Split('|');
            if (parts.Length != 3) return m;
            if (!ulong.TryParse(parts[2], out ulong hash)) return m;
            m.PlayerId   = parts[0];
            m.CloudStamp = parts[1];
            m.LocalHash  = hash;
            return m;
        }

        public static Marker LoadMarker() => ParseMarker(PlayerPrefs.GetString(MarkerKey, ""));

        public static void SaveMarker(string playerId, string cloudStamp, ulong localHash)
        {
            if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(cloudStamp)) return;
            PlayerPrefs.SetString(MarkerKey, FormatMarker(playerId, cloudStamp, localHash));
            PlayerPrefs.Save();
        }

        /// <summary>Restores a marker read earlier (a push that set it optimistically failed).</summary>
        public static void RestoreMarker(Marker m)
        {
            if (m.IsSet) PlayerPrefs.SetString(MarkerKey, FormatMarker(m.PlayerId, m.CloudStamp, m.LocalHash));
            else         PlayerPrefs.DeleteKey(MarkerKey);
            PlayerPrefs.Save();
        }

        /// <summary>Three short lines describing a save for the "which save?" choice.</summary>
        public static string Summary(SaveData save, DateTime nowUtc)
        {
            if (save == null) return "No save";
            int research = save.unlockedResearch?.Count ?? 0;
            string line1 = $"Prestige {save.prestigeCount}, {research} research";
            string line2 = $"Net worth {NumberFormat.Entropy(save.lastKnownNetWorth)}";
            return $"{line1}\n{line2}\nLast played {Ago(save.lastSaved, nowUtc)}";
        }

        /// <summary>"just now", "5 min ago", "3 h ago", "2 days ago", or "unknown".</summary>
        public static string Ago(string stamp, DateTime nowUtc)
        {
            if (string.IsNullOrEmpty(stamp) ||
                !DateTime.TryParse(stamp, System.Globalization.CultureInfo.InvariantCulture,
                                   System.Globalization.DateTimeStyles.RoundtripKind, out var t))
                return "unknown";
            if (t.Kind == DateTimeKind.Local) t = t.ToUniversalTime();
            var span = nowUtc - t;
            if (span.TotalMinutes < 1) return "just now";
            if (span.TotalHours   < 1) return $"{(int)span.TotalMinutes} min ago";
            if (span.TotalDays    < 1) return $"{(int)span.TotalHours} h ago";
            int days = (int)span.TotalDays;
            return days == 1 ? "1 day ago" : $"{days} days ago";
        }
    }
}
