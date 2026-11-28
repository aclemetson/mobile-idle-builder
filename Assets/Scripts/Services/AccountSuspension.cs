using System;
using Unity.Services.Core;

namespace MobileIdleBuilder
{
    /// <summary>
    /// A player banned in Unity Cloud (Player Management > Access: a deny policy) stays signed in, but every service
    /// call is refused with HTTP 403 and Unity error code 57, "Principal is not authorized to access resource" (seen on
    /// device 2026-11-28: Cloud Save load and push, player name). Without this the game looked merely offline and
    /// treated the refused cloud load as "no cloud save". Once a call reports that error, the session counts as
    /// suspended: cloud sync and tournament calls stop, Settings and a one-time card say so, and local play goes on.
    /// Not persisted: the next launch checks again, so lifting the ban needs only a relaunch.
    /// Runbook: docs/runbooks/player-support.md ("Ban a cheater").
    /// </summary>
    public static class AccountSuspension
    {
        /// <summary>Unity's Access Control error code for a denied request.</summary>
        public const int AccessDeniedCode = 57;
        const string AccessDeniedText = "Principal is not authorized to access resource";

        public static bool IsSuspended { get; private set; }

        /// <summary>Raised on the main thread the first time the session is found suspended.</summary>
        public static event Action Suspended;

        /// <summary>True for Unity's access-denied error, however the SDK wrapped it (inner/aggregate exceptions included).</summary>
        public static bool IsAccessDenied(Exception e)
        {
            for (int depth = 0; e != null && depth < 8; depth++)
            {
                if (e is RequestFailedException rf && rf.ErrorCode == AccessDeniedCode) return true;
                if (IsAccessDeniedText(e.Message)) return true;
                e = e is AggregateException agg && agg.InnerExceptions.Count > 0 ? agg.InnerExceptions[0] : e.InnerException;
            }
            return false;
        }

        /// <summary>The error text (or a raw error body containing "code":57) of a denied request.</summary>
        public static bool IsAccessDeniedText(string message) =>
            !string.IsNullOrEmpty(message) &&
            (message.Contains(AccessDeniedText) || message.Contains("\"code\":" + AccessDeniedCode + ","));

        /// <summary>Marks the session suspended when <paramref name="e"/> is an access denial. Returns whether it was.</summary>
        public static bool Report(Exception e)
        {
            if (!IsAccessDenied(e)) return false;
            MarkSuspended();
            return true;
        }

        public static void MarkSuspended()
        {
            if (IsSuspended) return;
            IsSuspended = true;
            GameLogger.Warning("[Account] Access denied by Unity (error 57): this account is suspended. " +
                               "Cloud saves and tournaments are off for this session.");
            try { Suspended?.Invoke(); }
            catch (Exception ex) { GameLogger.Error($"[Account] A Suspended handler failed: {ex}"); }
        }

        public const string Title = "Account suspended";

        /// <summary>The card and Settings text. <paramref name="supportEmail"/> may be empty.</summary>
        public static string Message(string supportEmail) =>
            "This account has been suspended from online features. You can keep playing on this device, but " +
            "cloud saves and tournaments are off." +
            (string.IsNullOrEmpty(supportEmail) ? "" : $"\n\nIf you think this is a mistake, email {supportEmail}.");

        /// <summary>Test seam: back to not suspended (also what a new launch starts with).</summary>
        internal static void ResetForTests() => IsSuspended = false;
    }
}
