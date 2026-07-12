using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
#if REMOTE_CONFIG
using Unity.Services.RemoteConfig;
#endif

namespace MobileIdleBuilder
{
    /// <summary>
    /// Externally-controlled feature flags backed by Unity Remote Config (UGS).
    ///
    /// Resolution order for every read: remote (fetched this session) &gt; on-disk cache
    /// (last known values) &gt; compile-time default in <see cref="FeatureFlags"/>. The cache is
    /// loaded synchronously in <see cref="Awake"/> so flags are correct from the previous session
    /// before the network fetch completes; <see cref="FetchAsync"/> then refreshes them for next launch.
    ///
    /// Never throws — a fetch failure (offline, UGS not initialized) simply leaves the cached/default
    /// values in place, mirroring <see cref="UGSCloudSaveService"/>'s graceful-degradation philosophy.
    ///
    /// Requires UGS to be initialized and signed in before <see cref="FetchAsync"/>; that already
    /// happens in <see cref="UGSCloudSaveService.InitializeAsync"/>, which SaveManager awaits first.
    /// The environment (development/production) is inherited from that same UGS init.
    /// </summary>
    [DefaultExecutionOrder(-80)]
    public class FeatureFlagService : SingletonMonoBehaviour<FeatureFlagService>
    {
        protected override bool PersistAcrossScenes => true;

        internal const string FileName = "feature_flags.json";

        string _filePath;
        readonly Dictionary<string, string> _values = new();

        /// <summary>True once a remote fetch has succeeded this session (vs. running on cache/defaults).</summary>
        public bool HasFetched { get; private set; }

        // Self-bootstrap: this service has no serialized fields, so it does not need scene placement
        // (and cannot be lost in a scene refactor — see the persistent-managers history). It is created
        // before any scene loads, well ahead of SaveManager's fetch and the HUD's first flag read.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            new GameObject(nameof(FeatureFlagService)).AddComponent<FeatureFlagService>();
        }

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;

            _filePath = Path.Combine(Application.persistentDataPath, FileName);
            LoadCache();
        }

        // ── Reads (remote/cache > default) ────────────────────────────────────

        public bool   GetBool  (string key, bool   fallback) => FeatureFlagResolver.ResolveBool  (_values, key, fallback);
        public float  GetFloat (string key, float  fallback) => FeatureFlagResolver.ResolveFloat (_values, key, fallback);
        public int    GetInt   (string key, int    fallback) => FeatureFlagResolver.ResolveInt   (_values, key, fallback);
        public string GetString(string key, string fallback) => FeatureFlagResolver.ResolveString(_values, key, fallback);

        // ── Fetch ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Fetches the latest flag values from Remote Config and persists them to the cache.
        /// Idempotent and safe to call once per launch. On any failure the in-memory values are
        /// left untouched (cache/defaults remain in effect). Call only after UGS init + sign-in.
        /// </summary>
        public async Task FetchAsync()
        {
            if (Application.internetReachability == NetworkReachability.NotReachable)
            {
                GameLogger.Info("[FeatureFlags] Offline — using cached/default values.");
                return;
            }

#if REMOTE_CONFIG
            try
            {
                // FetchConfigsAsync resolves once values are downloaded; the values themselves live on
                // RemoteConfigService.Instance.appConfig (the documented UGS pattern), not the return value.
                await RemoteConfigService.Instance.FetchConfigsAsync(new UserAttributes(), new AppAttributes());
                var appConfig = RemoteConfigService.Instance.appConfig;

                var fetched = new Dictionary<string, string>();
                foreach (var def in FeatureFlags.All)
                {
                    if (!appConfig.HasKey(def.Key)) continue;
                    fetched[def.Key] = def.Type switch
                    {
                        FlagType.Bool  => appConfig.GetBool (def.Key).ToString(),
                        FlagType.Float => appConfig.GetFloat(def.Key).ToString(CultureInfo.InvariantCulture),
                        FlagType.Int   => appConfig.GetInt  (def.Key).ToString(CultureInfo.InvariantCulture),
                        _              => appConfig.GetString(def.Key),
                    };
                }

                FeatureFlagResolver.MergeInto(_values, fetched);
                HasFetched = true;
                SaveCache();
                GameLogger.Info($"[FeatureFlags] Fetched {fetched.Count} flag(s) from Remote Config.");
            }
            catch (Exception ex)
            {
                GameLogger.Warning($"[FeatureFlags] Fetch failed — using cached/default values. Reason: {ex.Message}");
            }
#else
            GameLogger.Info("[FeatureFlags] Remote Config package not present — using cached/default values.");
            await Task.CompletedTask;
#endif
        }

        // ── Cache persistence ─────────────────────────────────────────────────

        [Serializable]
        class CacheDto
        {
            public List<string> keys   = new();
            public List<string> values = new();
        }

        void LoadCache()
        {
            try
            {
                if (!File.Exists(_filePath)) return;
                var dto = JsonUtility.FromJson<CacheDto>(File.ReadAllText(_filePath));
                if (dto?.keys == null || dto.values == null) return;
                int n = Math.Min(dto.keys.Count, dto.values.Count);
                for (int i = 0; i < n; i++)
                    _values[dto.keys[i]] = dto.values[i];
            }
            catch (Exception ex)
            {
                GameLogger.Warning($"[FeatureFlags] Cache load failed: {ex.Message}");
            }
        }

        void SaveCache()
        {
            try
            {
                var dto = new CacheDto();
                foreach (var kvp in _values) { dto.keys.Add(kvp.Key); dto.values.Add(kvp.Value); }
                File.WriteAllText(_filePath, JsonUtility.ToJson(dto, prettyPrint: false));
            }
            catch (Exception ex)
            {
                GameLogger.Warning($"[FeatureFlags] Cache save failed: {ex.Message}");
            }
        }

#if REMOTE_CONFIG
        // Remote Config requires attribute structs for targeting; we target by environment only,
        // so these are intentionally empty (populate later for app-version / audience rules).
        struct UserAttributes { }
        struct AppAttributes  { }
#endif
    }
}
