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
    /// Uses anonymous sign-in — no account required.
    /// Call InitializeAsync() once before FetchAsync / PushAsync.
    /// </summary>
    public class UGSCloudSaveService : ICloudSaveService
    {
        const string k_SaveKey = "save_v1";

        bool _initialized;
        bool _initFailed;

        public bool IsAvailable =>
            _initialized &&
            !_initFailed &&
            Application.internetReachability != NetworkReachability.NotReachable;

        /// <summary>
        /// Initializes Unity Services and signs in anonymously.
        /// Idempotent — subsequent calls return immediately.
        /// On any failure, IsAvailable stays false and the manager falls back to local-only.
        /// </summary>
        public async Task InitializeAsync()
        {
            if (_initialized || _initFailed) return;

            try
            {
                await UnityServices.InitializeAsync();

                if (!AuthenticationService.Instance.IsSignedIn)
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();

                _initialized = true;
                Debug.Log($"[UGSCloudSave] Initialized. PlayerId: {AuthenticationService.Instance.PlayerId}");
            }
            catch (Exception ex)
            {
                _initFailed = true;
                Debug.LogWarning($"[UGSCloudSave] Init failed — falling back to local-only. Reason: {ex.Message}");
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
                    Debug.Log("[UGSCloudSave] No cloud save found (first run on this account).");
                    return null;
                }

                string json = item.Value.GetAsString();
                var data = JsonUtility.FromJson<SaveData>(json);
                Debug.Log($"[UGSCloudSave] Fetched. lastSaved: {data?.lastSaved}");
                return data;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UGSCloudSave] FetchAsync failed: {ex.Message}");
                return null;
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
                Debug.Log($"[UGSCloudSave] Pushed. lastSaved: {data.lastSaved}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UGSCloudSave] PushAsync failed: {ex.Message}");
            }
        }
    }
}
