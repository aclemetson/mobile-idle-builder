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
    /// Uses Google Sign-In on Android device; falls back to anonymous in Editor and unsupported platforms.
    /// Call InitializeAsync() once before FetchAsync / PushAsync.
    /// </summary>
    public class UGSCloudSaveService : ICloudSaveService
    {
        const string k_SaveKey = "save_v1";

        bool _initialized;
        bool _initFailed;
        string _env;

        // Set by GoogleSignInBridge on Android before InitializeAsync is called.
        // Null = fall back to anonymous sign-in (Editor, unsupported platforms).
        public static Func<Task<string>> GoogleAuthProvider;

#if UNITY_EDITOR
        // Set by UGSEditorAuth before scene load. Calls SignInWithUsernamePasswordAsync internally.
        public static Func<Task> EditorAuthProvider;
#endif

        public bool IsAvailable =>
            _initialized &&
            !_initFailed &&
            Application.internetReachability != NetworkReachability.NotReachable;

        // Idempotent. On any failure IsAvailable stays false and SaveManager falls back to local-only.
        public async Task InitializeAsync()
        {
            if (_initialized || _initFailed) return;

            try
            {
#if UNITY_EDITOR || DEV_ENVIRONMENT
                _env = "development";
#else
                _env = "production";
#endif
                // "com.unity.services.core.environment-name" is the internal key used by
                // Unity.Services.Core.Environments.EnvironmentsOptionsExtensions.SetEnvironmentName.
                var initOptions = new InitializationOptions().SetOption("com.unity.services.core.environment-name", _env);
                await UnityServices.InitializeAsync(initOptions);

                bool sessionExists = AuthenticationService.Instance.SessionTokenExists;
                GameLogger.Info($"[UGSCloudSave] SessionTokenExists={sessionExists}");

                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    // Restore a cached UGS session first — avoids Google Sign-In on every cold start.
                    // SignInAnonymouslyAsync re-signs the existing player (Google-linked or not) when
                    // a session token is present, per UGS Auth docs.
                    if (sessionExists)
                    {
                        try
                        {
                            await AuthenticationService.Instance.SignInAnonymouslyAsync();
                            GameLogger.Info("[UGSCloudSave] Restored cached UGS session.");
                        }
                        catch (Exception sessionEx)
                        {
                            GameLogger.Info($"[UGSCloudSave] Cached session restore failed ({sessionEx.Message}); proceeding to Google Sign-In.");
                        }
                    }
                }

                if (!AuthenticationService.Instance.IsSignedIn)
                {
#if UNITY_EDITOR
                    if (EditorAuthProvider != null)
                        await EditorAuthProvider();
                    else
#endif
                    if (GoogleAuthProvider != null)
                    {
                        try
                        {
                            string idToken = await GoogleAuthProvider();
                            try
                            {
                                await AuthenticationService.Instance.SignInWithGoogleAsync(idToken);
                            }
                            catch (Exception googleEx)
                            {
                                // UGS Google provider may not be configured in the dashboard yet.
                                // Fall back to anonymous so the session token gets stored on device —
                                // this guarantees silent restore on the next cold start.
                                GameLogger.Warning($"[UGSCloudSave] SignInWithGoogleAsync failed ({googleEx.Message}); falling back to anonymous session.");
                                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                            }
                        }
                        catch (Exception silentEx)
                        {
                            // Google silent sign-in is unavailable (no cached credential, Play Services
                            // issue, etc.) and policy did not permit an interactive prompt.  Sign in
                            // anonymously so the session token is stored and future launches are silent.
                            // The 30/90-day policy will trigger a full interactive re-auth when due.
                            GameLogger.Info($"[UGSCloudSave] Google silent sign-in unavailable ({silentEx.Message}); using anonymous session.");
                            await AuthenticationService.Instance.SignInAnonymouslyAsync();
                        }
                    }
                    else
                    {
                        await AuthenticationService.Instance.SignInAnonymouslyAsync();
                    }
                }

                _initialized = true;
                GameLogger.Info($"[UGSCloudSave] Initialized. Env: {_env}  PlayerId: {AuthenticationService.Instance.PlayerId}");
            }
            catch (Exception ex)
            {
                _initFailed = true;
                GameLogger.Warning($"[UGSCloudSave] Init failed — falling back to local-only. Reason: {ex.Message}");
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
                await Unity.Services.CloudSave.CloudSaveService.Instance.Data.Player.DeleteAsync(k_SaveKey);
                GameLogger.Info("[UGSCloudSave] Cloud save deleted.");
            }
            catch (Exception ex)
            {
                GameLogger.Warning($"[UGSCloudSave] DeleteAsync failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>Pushes SaveData to UGS under key "save_v1".</summary>
        public async Task PushAsync(SaveData data)
        {
            if (!IsAvailable) return;

            try
            {
                string json    = JsonUtility.ToJson(data);
                var    payload = new Dictionary<string, object> { { k_SaveKey, json } };
                await Unity.Services.CloudSave.CloudSaveService.Instance.Data.Player.SaveAsync(payload);
                GameLogger.Info($"[UGSCloudSave] Pushed. lastSaved: {data.lastSaved}");
            }
            catch (Exception ex)
            {
                GameLogger.Warning($"[UGSCloudSave] PushAsync failed: {ex.Message}");
            }
        }
    }
}
