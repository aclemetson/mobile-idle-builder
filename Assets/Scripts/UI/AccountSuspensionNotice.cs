using System;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// The one-time "Account suspended" card (AccountSuspension). HUDController attaches it after ConfirmPrompt.Init;
    /// it shows as soon as both the HUD is ready and the session is found suspended, whichever comes last, and only
    /// once per launch (a scene reload re-attaches but does not show it again).
    /// </summary>
    public static class AccountSuspensionNotice
    {
        static bool s_shown;
        static bool s_subscribed;

        public static void Attach()
        {
            if (!s_subscribed)
            {
                AccountSuspension.Suspended += TryShow;
                s_subscribed = true;
            }
            TryShow();
        }

        static void TryShow()
        {
            if (s_shown || !AccountSuspension.IsSuspended || !ConfirmPrompt.IsReady) return;
            if (ConfirmPrompt.IsOpen) return;   // don't cancel the player's open question; Settings still says it
            s_shown = true;

            string email = FeatureFlags.FeedbackEmail;
            string message = AccountSuspension.Message(email);
            if (string.IsNullOrEmpty(email))
            {
                ConfirmPrompt.Show(AccountSuspension.Title, message, "OK", null, cancelLabel: "Close", destructive: false);
                return;
            }
            ConfirmPrompt.Show(AccountSuspension.Title, message, "Email support",
                () => Application.OpenURL(SupportMailto(email)), cancelLabel: "OK", destructive: false);
        }

        internal static string SupportMailto(string email) =>
            $"mailto:{email}?subject={Uri.EscapeDataString("Account suspended")}";

        /// <summary>Test seam: show again next time.</summary>
        internal static void ResetForTests() => s_shown = false;
    }
}
