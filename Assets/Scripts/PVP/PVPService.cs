using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace MobileIdleBuilder
{
    public enum PVPState { Locked, Available, InRun, Completed }

    [Serializable]
    public class LeaderboardEntry
    {
        public int    rank;
        public string playerId;
        public long   score;
    }

    // Internal DTO — not part of public API
    [Serializable]
    class LeaderboardResponse
    {
        public List<LeaderboardEntry> entries      = new();
        public bool                  newWeekOpen;  // server signals a new competition is available
    }

    [Serializable]
    class PVPSubmitRequest
    {
        public string playerId;
        public string runEndUtc;
    }

    /// <summary>
    /// Manages PVP competition state: unlock gate, 48-hour run timer,
    /// score submission, and leaderboard fetching.
    ///
    /// Call <see cref="MarkRunStarted"/> from HUDController after the ECS
    /// PVPRunRequested flag has been set.
    /// </summary>
    public class PVPService : SingletonMonoBehaviour<PVPService>
    {
        /// <summary>Minimum prestige runs required to unlock PVP mode.</summary>
        public const int PvpPrestigeUnlockCount = 2;

        /// <summary>Length of a single competition run in hours.</summary>
        public const double RunDurationHours = 48.0;

        [SerializeField] GameConfigSO  gameConfig;
        [SerializeField] HUDController hudController;

        protected override bool PersistAcrossScenes => false;

        /// <summary>Fired when State changes — HUD panel should rebuild.</summary>
        public event Action OnStateChanged;

        public PVPState State         { get; private set; } = PVPState.Locked;
        public TimeSpan TimeRemaining { get; private set; }

        bool _expiryTriggered;

        void Start() => RefreshState();

        void Update()
        {
            if (State != PVPState.InRun) return;

            UpdateTimeRemaining();

            if (TimeRemaining <= TimeSpan.Zero && !_expiryTriggered)
            {
                _expiryTriggered = true;
                StartCoroutine(ExpireRunAsync());
            }
        }

        // ── Public API ────────────────────────────────────────────────────────

        public bool CanEnterCompetition => State == PVPState.Available;

        /// <summary>
        /// Records the competition run start in save data.
        /// Call from HUDController AFTER setting the ECS PVPRunRequested flag
        /// so that both the save data and the ECS state update together.
        /// </summary>
        public void MarkRunStarted()
        {
            var save = SaveManager.Instance?.Current;
            if (save == null) return;

            var now = DateTime.UtcNow;
            save.pvpRun.state          = "InRun";
            save.pvpRun.runStartUtc    = now.ToString("o");
            save.pvpRun.runEndUtc      = now.AddHours(RunDurationHours).ToString("o");
            save.pvpRun.submittedScore = 0;

            SaveManager.Instance.SaveLocal();
            _expiryTriggered = false;
            RefreshState();
            OnStateChanged?.Invoke();
        }

        /// <summary>
        /// Resets state to Available for a new weekly competition window.
        /// Typically triggered after the server signals a new week is open.
        /// </summary>
        public void ResetForNewWeek()
        {
            var save = SaveManager.Instance?.Current;
            if (save == null) return;
            if (save.prestigeCount < PvpPrestigeUnlockCount) return;

            save.pvpRun = new PVPRunData();
            SaveManager.Instance.SaveLocal();
            _expiryTriggered = false;
            RefreshState();
            OnStateChanged?.Invoke();
        }

        /// <summary>
        /// Fetches the current competition leaderboard.
        /// Passes an empty list to <paramref name="callback"/> on failure.
        /// </summary>
        public IEnumerator FetchLeaderboardAsync(Action<List<LeaderboardEntry>> callback)
        {
            if (gameConfig == null || string.IsNullOrEmpty(gameConfig.apiBaseUrl))
            {
                callback?.Invoke(new List<LeaderboardEntry>());
                yield break;
            }

            string url = $"{gameConfig.apiBaseUrl.TrimEnd('/')}/pvp/leaderboard";
            using var req = UnityWebRequest.Get(url);
            var task = SendAsync(req);
            yield return new WaitUntil(() => task.IsCompleted);

            if (req.result != UnityWebRequest.Result.Success)
            {
                GameLogger.Warning($"[PVP] Leaderboard fetch failed: {req.error}");
                callback?.Invoke(new List<LeaderboardEntry>());
                yield break;
            }

            var response = JsonUtility.FromJson<LeaderboardResponse>(req.downloadHandler.text);

            if (response != null && response.newWeekOpen)
                ResetForNewWeek();

            callback?.Invoke(response?.entries ?? new List<LeaderboardEntry>());
        }

        /// <summary>Returns a human-readable countdown string, or "" if not in a run.</summary>
        public string FormatTimeRemaining()
        {
            if (State != PVPState.InRun) return "";
            var t = TimeRemaining;
            if (t <= TimeSpan.Zero) return "Expired";
            return t.TotalHours >= 1
                ? $"{(int)t.TotalHours}h {t.Minutes:D2}m"
                : $"{t.Minutes}m {t.Seconds:D2}s";
        }

        // ── Internal ──────────────────────────────────────────────────────────

        void RefreshState()
        {
            var save = SaveManager.Instance?.Current;
            if (save == null || save.prestigeCount < PvpPrestigeUnlockCount)
            {
                State = PVPState.Locked;
                return;
            }

            State = save.pvpRun.state switch
            {
                "InRun"     => PVPState.InRun,
                "Completed" => PVPState.Completed,
                _           => PVPState.Available,
            };

            if (State == PVPState.InRun)
                UpdateTimeRemaining();
        }

        void UpdateTimeRemaining()
        {
            var save = SaveManager.Instance?.Current;
            if (save == null || string.IsNullOrEmpty(save.pvpRun.runEndUtc))
            {
                TimeRemaining = TimeSpan.Zero;
                return;
            }

            var end = DateTime.Parse(
                save.pvpRun.runEndUtc, null,
                System.Globalization.DateTimeStyles.RoundtripKind);

            TimeRemaining = end - DateTime.UtcNow;
            if (TimeRemaining < TimeSpan.Zero) TimeRemaining = TimeSpan.Zero;
        }

        IEnumerator ExpireRunAsync()
        {
            var save = SaveManager.Instance?.Current;
            if (save == null) yield break;

            // Score is calculated server-side from production logs — anti-cheat requirement.
            // Client submits run-end timestamp only.
            save.pvpRun.state = "Completed";
            SaveManager.Instance.SaveLocal();
            RefreshState();
            OnStateChanged?.Invoke();

            AchievementService.Instance?.NotifyPVPWin();
            hudController?.ShowNotification("⚔", "Competition run ended! Score is being calculated.", "warning");

            if (Application.internetReachability != NetworkReachability.NotReachable &&
                gameConfig != null && !string.IsNullOrEmpty(gameConfig.apiBaseUrl))
            {
                yield return PostRunEndToServer(save.playerId, save.pvpRun.runEndUtc);
            }
        }

        IEnumerator PostRunEndToServer(string playerId, string runEndUtc)
        {
            string url  = $"{gameConfig.apiBaseUrl.TrimEnd('/')}/pvp/submit";
            byte[] body = Encoding.UTF8.GetBytes(
                JsonUtility.ToJson(new PVPSubmitRequest
                {
                    playerId  = playerId,
                    runEndUtc = runEndUtc
                }));

            using var req = new UnityWebRequest(url, "POST")
            {
                uploadHandler   = new UploadHandlerRaw(body),
                downloadHandler = new DownloadHandlerBuffer()
            };
            req.SetRequestHeader("Content-Type", "application/json");

            var task = SendAsync(req);
            yield return new WaitUntil(() => task.IsCompleted);

            if (req.result != UnityWebRequest.Result.Success)
                GameLogger.Warning($"[PVP] Score submission failed: {req.error}");
            else
                GameLogger.Info($"[PVP] Score submission sent for {playerId}");
        }

        static Task SendAsync(UnityWebRequest req)
        {
            var tcs = new TaskCompletionSource<bool>();
            req.SendWebRequest().completed += _ => tcs.SetResult(true);
            return tcs.Task;
        }
    }
}
