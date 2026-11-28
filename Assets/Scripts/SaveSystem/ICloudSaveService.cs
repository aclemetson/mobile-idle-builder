using System;
using System.Threading.Tasks;

namespace MobileIdleBuilder
{
    public interface ICloudSaveService
    {
        /// <summary>One-time async init (UGS auth + service setup). Must complete before Fetch/Push.</summary>
        Task InitializeAsync();

        /// <summary>Fetch the latest save from the cloud. Returns null if not found.</summary>
        Task<SaveData> FetchAsync(string playerId);

        /// <summary>
        /// Push local save to the cloud. Never throws; resolves to true only if the write reached the
        /// cloud. The SaveData must be serialized synchronously, before the first await, so a caller
        /// that is about to be suspended (app backgrounded) still pushes the state it had at call time.
        /// </summary>
        Task<bool> PushAsync(SaveData data);

        /// <summary>Returns true if a network connection is available and init succeeded.</summary>
        bool IsAvailable { get; }

        /// <summary>Delete the cloud save key for the signed-in player. No-op if unavailable.</summary>
        Task DeleteAsync();

        // ── Account (cross-device saves) ─────────────────────────────────────

        /// <summary>Guest, Google, ... for the Settings account row and the sync rules.</summary>
        CloudAccountKind AccountKind { get; }

        /// <summary>Google email when signed in with Google (may be null if never reported).</summary>
        string AccountEmail { get; }

        /// <summary>UGS player id the cloud save is stored under, or null when not signed in.</summary>
        string AccountPlayerId { get; }

        /// <summary>Why the last Google sign-in attempt failed (null if it didn't).</summary>
        string LastSignInError { get; }

        /// <summary>
        /// True when this launch moved the device from a guest player to an existing Google player
        /// (another device's save), so the local save and that cloud save are unrelated.
        /// </summary>
        bool AccountSwitched { get; }

        /// <summary>
        /// Attaches Google to the signed-in player, or switches to the player that already owns this
        /// Google account. Never throws.
        /// </summary>
        Task<GoogleSignInOutcome> SignInWithGoogleAsync(GoogleAuthMode mode);

        /// <summary>
        /// Settings "Delete account": deletes the signed-in UGS account (and its Google link) and signs out,
        /// leaving no session to resume. Throws when the delete fails (the account is then untouched).
        /// </summary>
        Task DeleteAccountAsync();

        /// <summary>
        /// After <see cref="DeleteAccountAsync"/> and the local wipe: signs in a brand-new guest so the session keeps
        /// working without a relaunch, and stops silent Google linking. Never throws; false = offline until relaunch.
        /// </summary>
        Task<bool> StartNewGuestAsync();
    }
}
