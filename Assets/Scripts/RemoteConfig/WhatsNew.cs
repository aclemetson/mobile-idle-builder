using System;
using System.Text;

namespace MobileIdleBuilder
{
    /// <summary>
    /// What's-new notice after an update (#502). CI publishes the release's player-facing notes
    /// (docs/whats-new.md) to Remote Config as <c>whatsnew.notes</c>, with the build's version as
    /// <c>whatsnew.version</c>, in one write when integration merges into release. The client shows them once:
    ///  - only on a build at least as new as the notes (a player who hasn't updated yet doesn't see them),
    ///  - only once per text (the save keeps a hash, so a hotfix release with unchanged notes stays quiet),
    ///  - never to a brand-new player (nothing "changed" for them; the marker is set silently).
    /// Pure so the rules are unit-tested; HUDController.MaybeShowGameUpdateNotice wires it to the modal.
    /// </summary>
    public static class WhatsNew
    {
        public enum Decision { Hide, Show, MarkSeenSilently }

        public static Decision Decide(string appVersion, string notesVersion, string notes, string lastSeenHash,
                                      bool newPlayer)
        {
            if (string.IsNullOrWhiteSpace(notes)) return Decision.Hide;
            if (!AppUpdateCheck.TryParse(notesVersion, out var forVersion)) return Decision.Hide;
            if (!AppUpdateCheck.TryParse(appVersion, out var installed) || installed < forVersion) return Decision.Hide;
            if (Hash(notes) == lastSeenHash) return Decision.Hide;
            return newPlayer ? Decision.MarkSeenSilently : Decision.Show;
        }

        /// <summary>"What's new in 0.6.90".</summary>
        public static string Title(string notesVersion) =>
            string.IsNullOrWhiteSpace(notesVersion) ? "What's new" : $"What's new in {notesVersion.Trim()}";

        /// <summary>Stable FNV-1a hash of the trimmed notes (line endings normalised), as 8 hex digits.</summary>
        public static string Hash(string notes)
        {
            string text = (notes ?? "").Replace("\r\n", "\n").Trim();
            uint h = 2166136261;
            foreach (byte b in Encoding.UTF8.GetBytes(text))
            {
                h ^= b;
                h *= 16777619;
            }
            return h.ToString("x8");
        }
    }
}
