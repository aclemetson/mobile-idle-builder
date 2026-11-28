using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.CloudSave;
using Unity.Services.Core;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// ICloudSaveService backed by Unity Gaming Services Cloud Save.
    /// Signs in with Google on Android and iOS devices (GoogleSignInBridge); anonymous (guest) in the
    /// Editor and on unsupported platforms, or when Google sign-in fails.
    ///
    /// Cross-device saves (docs/agents/save-system.md): a device that ends up as a guest keeps trying a
    /// silent Google sign-in on later launches. Google is linked to the guest player when the Google
    /// account is new to the game, or the device switches to the player that already owns that Google
    /// account (<see cref="AccountSwitched"/>; SaveManager then asks which save to keep).
    /// Call InitializeAsync() once before FetchAsync / PushAsync.
    /// </summary>
    public class UGSCloudSaveService : ICloudSaveService
    {
        const string k_SaveKey = "save_v1";
        const string k_GoogleProvider = "google.com";

        /// <summary>PlayerPrefs: email of the Google account last signed in on this device (display only).</summary>
        public const string k_GoogleEmailKey = "auth_google_email";

        /// <summary>
        /// PlayerPrefs: set after the player deleted their account. A guest normally links Google silently on
        /// launch, which would quietly attach the deleted player's Google account to the new guest. While set,
        /// Google is linked only from Settings "Sign in with Google" (which clears it).
        /// </summary>
        public const string k_GoogleAutoLinkOffKey = "auth_google_autolink_off";

        internal static bool GoogleAutoLinkOff => PlayerPrefs.GetInt(k_GoogleAutoLinkOffKey, 0) == 1;

        bool _initialized;
        bool _initFailed;
        bool _hasGoogle;
        string _env;

        // Set by GoogleSignInBridge on Android / iOS devices before InitializeAsync is called.
        // Null = fall back to anonymous sign-in (Editor, unsupported platforms).
        public static Func<GoogleAuthMode, Task<GoogleCredential>> GoogleAuthProvider;

#if UNITY_EDITOR
        // Set by UGSEditorAuth before scene load. Calls SignInWithUsernamePasswordAsync internally.
        public static Func<Task> EditorAuthProvider;
        bool _editorAccount;
#endif

        public bool IsAvailable =>
            _initialized &&
            !_initFailed &&
            Application.internetReachability != NetworkReachability.NotReachable;

        public CloudAccountKind AccountKind
        {
            get
            {
                if (!_initialized || _initFailed || !AuthenticationService.Instance.IsSignedIn) return CloudAccountKind.None;
                if (_hasGoogle) return CloudAccountKind.Google;
#if UNITY_EDITOR
                if (_editorAccount) return CloudAccountKind.Other;
#endif
                return CloudAccountKind.Guest;
            }
        }

        public string AccountEmail => _hasGoogle ? PlayerPrefs.GetString(k_GoogleEmailKey, null) : null;

        public string AccountPlayerId =>
            _initialized && !_initFailed ? AuthenticationService.Instance.PlayerId : null;

        public string LastSignInError { get; private set; }
        public bool   AccountSwitched { get; private set; }

        // Idempotent. On any failure IsAvailable stays false and SaveManager falls back to local-only.
        public async Task InitializeAsync()
        {
            if (_initialized || _initFailed) return;

            try
            {
                _env = UgsEnvironment.Name;
                // "com.unity.services.core.environment-name" is the internal key used by
                // Unity.Services.Core.Environments.EnvironmentsOptionsExtensions.SetEnvironmentName.
                var initOptions = new InitializationOptions().SetOption("com.unity.services.core.environment-name", _env);
                await UnityServices.InitializeAsync(initOptions);

                var auth = AuthenticationService.Instance;
                bool sessionExists = auth.SessionTokenExists;
                // The splash's MaintenanceGate signs in from the cached session before this runs, so an
                // already-signed-in session is a restored one too.
                bool signedInBefore = auth.IsSignedIn;
                GameLogger.Info($"[UGSCloudSave] SessionTokenExists={sessionExists}  AlreadySignedIn={signedInBefore}");

                // Restore a cached UGS session first, which avoids Google Sign-In on every cold start.
                // SignInAnonymouslyAsync re-signs the existing player (Google-linked or not) when a
                // session token is present, per UGS Auth docs.
                bool restored = false;
                if (!auth.IsSignedIn && sessionExists)
                {
                    try
                    {
                        await auth.SignInAnonymouslyAsync();
                        restored = true;
                        GameLogger.Info("[UGSCloudSave] Restored cached UGS session.");
                    }
                    catch (Exception sessionEx)
                    {
                        GameLogger.Info($"[UGSCloudSave] Cached session restore failed ({sessionEx.Message}); proceeding to Google Sign-In.");
                    }
                }

                if (!auth.IsSignedIn)
                {
#if UNITY_EDITOR
                    if (EditorAuthProvider != null)
                    {
                        await EditorAuthProvider();
                        _editorAccount = true;
                    }
                    else
#endif
                    if (GoogleAuthProvider != null)
                        await SignInFreshAsync();
                    else
                        await auth.SignInAnonymouslyAsync();
                }

                _initialized = true;

                // Either way the session was resumed from the cached token, which may belong to a
                // Google-linked player. Checking only when this method resumed it left the splash-resumed
                // session marked Guest on every launch, so a Google sign-in never seemed to stick.
                if (restored || signedInBefore)
                {
                    _hasGoogle = await HasGoogleIdentityAsync();
                    // A device that fell back to guest once used to stay a guest forever, with its own
                    // save. Keep trying a silent Google sign-in (no UI) so it rejoins the Google account.
                    if (!_hasGoogle && GoogleAuthProvider != null)
                    {
                        var outcome = await SignInWithGoogleAsync(GoogleAuthMode.Silent);
                        GameLogger.Info($"[UGSCloudSave] Guest session; silent Google sign-in: {outcome}" +
                                        (LastSignInError != null ? $" ({LastSignInError})" : ""));
                    }
                }

                GameLogger.Info($"[UGSCloudSave] Initialized. Env: {_env}  PlayerId: {auth.PlayerId}  " +
                                $"Account: {AccountKind}{(AccountSwitched ? " (switched from guest)" : "")}");
            }
            catch (Exception ex)
            {
                _initialized = false;
                _initFailed = true;
                GameLogger.Warning($"[UGSCloudSave] Init failed — falling back to local-only. Reason: {ex.Message}");
            }
        }

        // No cached session: sign in with Google (account picker when AuthSessionPolicy asks for it), or
        // as a guest if Google is unavailable. A guest is retried silently on every later launch and
        // from Settings, so the fallback is no longer permanent.
        async Task SignInFreshAsync()
        {
            var auth = AuthenticationService.Instance;
            if (GoogleAutoLinkOff)
            {
                await auth.SignInAnonymouslyAsync();
                return;
            }
            GoogleCredential cred;
            try
            {
                cred = await GoogleAuthProvider(GoogleAuthMode.Policy);
            }
            catch (Exception silentEx)
            {
                LastSignInError = silentEx.Message;
                GameLogger.Info($"[UGSCloudSave] Google sign-in unavailable ({silentEx.Message}); using a guest session.");
                await auth.SignInAnonymouslyAsync();
                return;
            }

            try
            {
                await auth.SignInWithGoogleAsync(cred.IdToken);
                _hasGoogle = true;
                RememberEmail(cred.Email);
            }
            catch (Exception googleEx)
            {
                // Usually the UGS Google identity provider rejecting the token (e.g. the iOS client id
                // is not in its allowed list). Guest keeps the save on this device until it's fixed.
                LastSignInError = googleEx.Message;
                GameLogger.Warning($"[UGSCloudSave] SignInWithGoogleAsync failed ({googleEx.Message}); using a guest session.");
                await auth.SignInAnonymouslyAsync();
            }
        }

        public async Task<GoogleSignInOutcome> SignInWithGoogleAsync(GoogleAuthMode mode)
        {
            if (!_initialized || _initFailed || GoogleAuthProvider == null)
            {
                LastSignInError = GoogleAuthProvider == null ? "Google sign-in is not available on this platform." : "Not connected.";
                return GoogleSignInOutcome.Failed;
            }
            if (_hasGoogle) return GoogleSignInOutcome.AlreadyGoogle;
            // After an account deletion only the player's own tap in Settings links Google again.
            if (mode != GoogleAuthMode.Interactive && GoogleAutoLinkOff)
            {
                LastSignInError = null;
                return GoogleSignInOutcome.Failed;
            }

            var auth = AuthenticationService.Instance;
            GoogleCredential cred;
            try
            {
                cred = await GoogleAuthProvider(mode);
            }
            catch (Exception ex)
            {
                LastSignInError = ex.Message;
                return GoogleSignInOutcome.Failed;
            }

            try
            {
                // Not forceLink: that would take the Google account away from the player that owns it.
                await auth.LinkWithGoogleAsync(cred.IdToken);
                _hasGoogle = true;
                LastSignInError = null;
                RememberEmail(cred.Email);
                GameLogger.Info($"[UGSCloudSave] Linked Google to guest player {auth.PlayerId}.");
                return GoogleSignInOutcome.Linked;
            }
            catch (AuthenticationException ex) when (ex.ErrorCode == AuthenticationErrorCodes.AccountAlreadyLinked)
            {
                // The Google account already has a player (the other device's progress): switch to it.
                // SignOut keeps the guest's session token, so a failed switch can restore the guest.
                string guestId = auth.PlayerId;
                auth.SignOut();
                try
                {
                    await auth.SignInWithGoogleAsync(cred.IdToken);
                    _hasGoogle = true;
                    LastSignInError = null;
                    RememberEmail(cred.Email);

                    // The Google account was already linked to THIS player (the session just wasn't known to
                    // be Google). Same player, same cloud save: not a switch, so nothing to choose between.
                    if (!IsAccountSwitch(guestId, auth.PlayerId))
                    {
                        GameLogger.Info($"[UGSCloudSave] Google was already linked to this player ({auth.PlayerId}).");
                        return GoogleSignInOutcome.AlreadyGoogle;
                    }

                    AccountSwitched = true;
                    GameLogger.Info($"[UGSCloudSave] Google account already used by {auth.PlayerId}; switched from guest {guestId}.");
                    return GoogleSignInOutcome.SwitchedAccount;
                }
                catch (Exception switchEx)
                {
                    LastSignInError = switchEx.Message;
                    GameLogger.Warning($"[UGSCloudSave] Switching to the Google player failed ({switchEx.Message}); restoring guest.");
                    if (!auth.IsSignedIn) await auth.SignInAnonymouslyAsync();
                    return GoogleSignInOutcome.Failed;
                }
            }
            catch (Exception ex)
            {
                LastSignInError = ex.Message;
                GameLogger.Warning($"[UGSCloudSave] LinkWithGoogleAsync failed: {ex.Message}");
                return GoogleSignInOutcome.Failed;
            }
        }

        /// <summary>
        /// True when signing in with Google moved the device to a different player. Signing back in as the
        /// same player (Google was already linked to it) is not a switch.
        /// </summary>
        internal static bool IsAccountSwitch(string previousPlayerId, string newPlayerId) =>
            !string.IsNullOrEmpty(newPlayerId) && newPlayerId != previousPlayerId;

        static async Task<bool> HasGoogleIdentityAsync()
        {
            var auth = AuthenticationService.Instance;
            if (HasGoogle(auth.PlayerInfo)) return true;
            try
            {
                return HasGoogle(await auth.GetPlayerInfoAsync());
            }
            catch (Exception ex)
            {
                GameLogger.Warning($"[UGSCloudSave] GetPlayerInfoAsync failed ({ex.Message}); treating as guest.");
                return false;
            }
        }

        static bool HasGoogle(PlayerInfo info)
        {
            if (info?.Identities == null) return false;
            foreach (var id in info.Identities)
                if (id?.TypeId == k_GoogleProvider) return true;
            return false;
        }

        static void RememberEmail(string email)
        {
            // Google is attached again (only the Settings button gets here while the flag is set).
            PlayerPrefs.DeleteKey(k_GoogleAutoLinkOffKey);
            if (string.IsNullOrEmpty(email)) { PlayerPrefs.Save(); return; }
            PlayerPrefs.SetString(k_GoogleEmailKey, email);
            PlayerPrefs.Save();
        }

        public async Task DeleteAccountAsync()
        {
            if (!_initialized || _initFailed) throw new InvalidOperationException("Not connected.");
            var auth = AuthenticationService.Instance;
            if (!auth.IsSignedIn) throw new InvalidOperationException("Not signed in.");

            string deletedId = auth.PlayerId;
            await auth.DeleteAccountAsync();   // throws on failure: the account and its Google link are untouched
            GameLogger.Info($"[UGSCloudSave] Deleted account {deletedId}.");

            _hasGoogle = false;
            AccountSwitched = false;
            LastSignInError = null;
            PlayerPrefs.DeleteKey(k_GoogleEmailKey);
            PlayerPrefs.SetInt(k_GoogleAutoLinkOffKey, 1);
            PlayerPrefs.Save();
            if (auth.IsSignedIn) auth.SignOut(true);
            if (auth.SessionTokenExists) auth.ClearSessionToken();   // never resume the deleted player
        }

        public async Task<bool> StartNewGuestAsync()
        {
            // Set again here: the local wipe between the two steps clears every PlayerPrefs key.
            PlayerPrefs.SetInt(k_GoogleAutoLinkOffKey, 1);
            PlayerPrefs.Save();
            try
            {
                var auth = AuthenticationService.Instance;
                if (auth.IsSignedIn) auth.SignOut(true);
                await auth.SignInAnonymouslyAsync();
                _initialized = true;
                _initFailed = false;
                GameLogger.Info($"[UGSCloudSave] New guest player {auth.PlayerId} after account deletion.");
                return true;
            }
            catch (Exception ex)
            {
                // Offline for the rest of the session; the next launch signs in a fresh guest.
                _initialized = false;
                _initFailed = true;
                GameLogger.Warning($"[UGSCloudSave] Guest sign-in after account deletion failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Fetches the remote save. Returns null if key does not exist or on any error.
        /// playerId param is ignored — UGS scopes data to the signed-in player automatically.
        /// </summary>
        public async Task<SaveData> FetchAsync(string playerId)
        {
            if (!IsAvailable) return null;

            try
            {
                var keys   = new HashSet<string> { k_SaveKey };
                var result = await Unity.Services.CloudSave.CloudSaveService.Instance.Data.Player.LoadAsync(keys);

                if (!result.TryGetValue(k_SaveKey, out var item))
                {
                    GameLogger.Info("[UGSCloudSave] No cloud save found (first run on this account).");
                    return null;
                }

                string json = item.Value.GetAsString();
                var data = JsonUtility.FromJson<SaveData>(json);
                GameLogger.Info($"[UGSCloudSave] Fetched. lastSaved: {data?.lastSaved}");
                return data;
            }
            catch (Exception ex)
            {
                GameLogger.Warning($"[UGSCloudSave] FetchAsync failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>Deletes the cloud save key for the signed-in player.</summary>
        public async Task DeleteAsync()
        {
            if (!IsAvailable) return;

            try
            {
                await Unity.Services.CloudSave.CloudSaveService.Instance.Data.Player.DeleteAsync(
                    k_SaveKey, new Unity.Services.CloudSave.Models.Data.Player.DeleteOptions());
                GameLogger.Info("[UGSCloudSave] Cloud save deleted.");
            }
            catch (Unity.Services.CloudSave.CloudSaveException ex) when (IsNotFound(ex))
            {
                // Nothing stored under the key (never pushed, or already wiped) — the goal state.
                GameLogger.Info("[UGSCloudSave] No cloud save to delete.");
            }
            catch (Exception ex)
            {
                GameLogger.Warning($"[UGSCloudSave] DeleteAsync failed: {ex.Message}");
                throw;
            }
        }

        private static bool IsNotFound(Unity.Services.CloudSave.CloudSaveException ex) =>
            ex.Reason == Unity.Services.CloudSave.CloudSaveExceptionReason.NotFound
            || ex.ErrorCode == 404
            || (ex.Message?.Contains("404") ?? false);

        /// <summary>Pushes SaveData to UGS under key "save_v1".</summary>
        public async Task<bool> PushAsync(SaveData data)
        {
            if (!IsAvailable) return false;

            try
            {
                string json    = JsonUtility.ToJson(data);
                var    payload = new Dictionary<string, object> { { k_SaveKey, json } };
                await Unity.Services.CloudSave.CloudSaveService.Instance.Data.Player.SaveAsync(payload);
                GameLogger.Info($"[UGSCloudSave] Pushed. lastSaved: {data.lastSaved}");
                return true;
            }
            catch (Exception ex)
            {
                GameLogger.Warning($"[UGSCloudSave] PushAsync failed: {ex.Message}");
                return false;
            }
        }
    }
}
