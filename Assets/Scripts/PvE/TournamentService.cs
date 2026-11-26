using System;
using System.Threading.Tasks;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// The player's side of the weekly PvE tournament (PvE phase 4; spec docs/agents/tasks/feature-pve-tournaments.md).
    ///  - Keeps the last server status (<see cref="LastStatus"/>) and mirrors the entered tournament into
    ///    <see cref="SaveData.tournament"/>, so the run timer and the prestige lock also work offline and a
    ///    reinstall or cloud swap gets them back from the server.
    ///  - <see cref="JoinAsync"/> enters on the server; the caller (the Tournament panel) then runs the normal
    ///    prestige through <see cref="HUDController.RequestTournamentPrestige"/>. If the app dies in between,
    ///    <see cref="JoinPrestigePending"/> lets the HUD finish that prestige on the next load.
    ///  - Sends snapshots (net worth, idle net worth rate, idle cap) every 5 minutes of play, on load, on resume,
    ///    when the app goes to the background, and once at the 24h mark. Never while the join's prestige is
    ///    still pending (<see cref="TournamentRules.PrestigePending"/>).
    /// Self-bootstrapped and persistent, like WorldService: no scene wiring.
    /// </summary>
    public class TournamentService : MonoBehaviour
    {
        public static TournamentService Instance { get; private set; }

        /// <summary>Raised on the main thread when the status, the entry or the run changes.</summary>
        public event Action OnChanged;

        /// <summary>The last server status (null until the first successful call).</summary>
        public PveStatusResponse LastStatus { get; private set; }

        float _sinceSnapshot;
        float _pendingRetryAt;          // unscaled time before which a failed delayed delivery isn't retried
        bool  _sending;
        bool  _forceSnapshot = true;    // first snapshot as soon as the run's scene is loaded
        bool  _subscribed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject(nameof(TournamentService));
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<TournamentService>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (_subscribed && SaveManager.Instance != null) SaveManager.Instance.Backgrounding -= OnBackgrounding;
            if (Instance == this) Instance = null;
        }

        // ── State ─────────────────────────────────────────────────────────

        /// <summary>Server time as far as the client knows it (the dev console's debug time wins).</summary>
        public static DateTime Now => PveApi.DebugNowUtc ?? ServerClock.UtcNow ?? DateTime.UtcNow;

        static SaveData Save => SaveManager.Instance != null ? SaveManager.Instance.Current : null;

        public static bool Enabled => FeatureFlags.PveEnabled;

        public bool Unlocked => TournamentRules.IsUnlocked(Save);

        public TournamentSaveData Entry => Save?.tournament;

        public bool InRun => TournamentRules.InRun(Entry, Now);

        public TimeSpan RunRemaining => TournamentRules.Remaining(Entry, Now);

        /// <summary>A prestige or Ascension now would end the run early (the HUD asks, then <see cref="RetireAsync"/>).</summary>
        public bool PrestigeLeavesRun => TournamentRules.PrestigeLeavesRun(Entry, Now);

        /// <summary>Joined, but the join's prestige hasn't run (the app closed in between).</summary>
        public bool JoinPrestigePending => Save != null && InRun && TournamentRules.PrestigePending(Entry, Save.prestigeCount);

        public TournamentPanelState PanelState =>
            TournamentRules.PanelState(Enabled, Unlocked, LastStatus, Entry, Now);

        // ── Server calls ──────────────────────────────────────────────────

        /// <summary>Fetches the server status and mirrors the current entry into the save.</summary>
        public async Task<PveStatusResponse> RefreshAsync()
        {
            var status = await PveApi.Status();
            if (string.IsNullOrEmpty(status.error))
            {
                LastStatus = status;
                MirrorEntry(status);
                await FinishServerClaims(status);
                await SyncLookIfChanged(status);
            }
            else if (LastStatus == null || status.error.StartsWith("network:") == false)
            {
                LastStatus = status;
            }
            OnChanged?.Invoke();
            return status;
        }

        /// <summary>
        /// Enters this week's tournament on the server. On success the save records the entry with the
        /// prestige still pending; the caller must then request the prestige.
        /// </summary>
        public async Task<PveStatusResponse> JoinAsync()
        {
            var save = Save;
            if (save == null) return new PveStatusResponse { error = "network:no save" };
            var status = await PveApi.Join(save.devConsoleUsed);
            if (!string.IsNullOrEmpty(status.error)) { OnChanged?.Invoke(); return status; }

            LastStatus = status;
            save.tournament = new TournamentSaveData
            {
                week = status.week,
                bracketId = status.bracketId,
                league = status.profile?.league,
                startUtc = status.startUtc,
                runEndsUtc = status.runEndsUtc,
                prestigeCountAtJoin = save.prestigeCount,
            };
            SaveManager.Instance.SaveLocal();
            _forceSnapshot = true;
            GameLogger.Info($"[PvE] Joined {status.bracketId}, run until {status.runEndsUtc}.");
            TelemetryService.Instance?.RecordPveLeague("joined", LeagueTier(status.profile?.league), 0);
            OnChanged?.Invoke();
            return status;
        }

        /// <summary>
        /// Leaves the run early at the current net worth (#505). On success (or when the server says the run is
        /// already over / already retired) the entry is marked retired: no more snapshots, the kept one is dropped,
        /// prestige is free again. Returns the server error, or null on success; a network error changes nothing.
        /// </summary>
        public async Task<string> RetireAsync()
        {
            var save = Save;
            var t = save?.tournament;
            if (t == null || !TournamentRules.InRun(t, Now)) return null;
            var (nw, _, _) = SnapshotValues();
            _sending = true;
            PveSnapshotResponse r;
            try { r = await PveApi.Retire(nw); }
            finally { _sending = false; }
            if (!string.IsNullOrEmpty(r.error) && r.error.StartsWith("network:")) return r.error;
            MarkRetired(t);
            GameLogger.Info($"[PvE] Retired at nw={nw:N0} ({(string.IsNullOrEmpty(r.error) ? "ok" : r.error)}).");
            OnChanged?.Invoke();
            return string.IsNullOrEmpty(r.error) || r.error == PveErrors.Retired || r.error == PveErrors.RunOver ? null : r.error;
        }

        static void MarkRetired(TournamentSaveData t)
        {
            t.retired = true;
            t.finalSent = true;
            t.pendingT = -1;
            SaveManager.Instance.SaveLocal();
        }

        /// <summary>What a claim gave, for the reveal card.</summary>
        public sealed class ClaimOutcome
        {
            public PveReward reward;
            public System.Collections.Generic.List<string> starsLanded = new System.Collections.Generic.List<string>();
            public bool applied;            // false = this save had already received it (only the server step ran)
            public string serverError;      // non-empty when the server step failed (it is retried on the next refresh)
        }

        /// <summary>
        /// Claims a tournament reward. Applied to the save first (crystals, stars on discovered items, the week
        /// recorded) and saved, then cleared on the server. A failed server step leaves the reward listed there;
        /// <see cref="RefreshAsync"/> clears it later without granting it again (<see cref="TournamentRules.RewardApplied"/>).
        /// </summary>
        public async Task<ClaimOutcome> ClaimAsync(PveReward reward)
        {
            var outcome = new ClaimOutcome { reward = reward };
            var save = Save;
            if (save == null || reward == null) return outcome;
            if (!TournamentRules.RewardApplied(save, reward.week))
            {
                var pool = ItemStars.DiscoveredItemIds(RecipeDatabase.Instance?.Recipes, RecipeKnowledgeService.Current);
                outcome.starsLanded = TournamentRules.ApplyReward(save, reward, pool, new System.Random());
                outcome.applied = true;
                SaveManager.Instance.SaveLocal();
                GameLogger.Info($"[PvE] Reward {reward.week} applied: {reward.crystals} crystals, {outcome.starsLanded.Count} stars, {reward.strings} Strings.");
                // Once per week: the RewardApplied guard above keeps a re-claim from reporting again.
                TelemetryService.Instance?.RecordPveLeague("finished", LeagueTier(reward.league), reward.rank);
            }
            var r = await PveApi.Claim(reward.week);
            if (!string.IsNullOrEmpty(r.error) && r.error != PveErrors.NothingToClaim) outcome.serverError = r.error;
            await RefreshAsync();
            return outcome;
        }

        /// <summary>The league's position in the game_config.pve ladder (0 = first), or -1 when unknown.</summary>
        public static int LeagueTier(string leagueId) =>
            LeagueTier(GameBootstrap.Instance != null && GameBootstrap.Instance.gameConfig != null
                ? GameBootstrap.Instance.gameConfig.pve : null, leagueId);

        public static int LeagueTier(PveConfig cfg, string leagueId)
        {
            var leagues = cfg?.leagues;
            if (leagues == null || string.IsNullOrEmpty(leagueId)) return -1;
            for (int i = 0; i < leagues.Length; i++)
                if (leagues[i] != null && leagues[i].id == leagueId) return i;
            return -1;
        }

        bool _lookSyncing;

        /// <summary>
        /// Pushes the equipped name colour and title (#500) when the server's profile differs. Called after every
        /// status refresh and after an equip, so a call that failed offline lands later.
        /// </summary>
        public async Task SyncLookIfChanged(PveStatusResponse status = null)
        {
            var save = Save;
            var profile = (status ?? LastStatus)?.profile;
            if (save == null || profile == null || _lookSyncing) return;
            if (!TournamentCosmetics.LookDiffers(save, profile.nameColor, profile.title)) return;
            _lookSyncing = true;
            try
            {
                var r = await PveApi.SetLook(save.pveNameColor ?? "", save.pveTitle ?? "");
                if (string.IsNullOrEmpty(r.error)) LastStatus = r;
                else GameLogger.Warning($"[PvE] SetLook failed: {r.error}");
            }
            finally { _lookSyncing = false; }
        }

        // Rewards this save already applied but the server still lists (its Claim never landed): clear them there.
        async Task FinishServerClaims(PveStatusResponse status)
        {
            var list = status.profile?.pendingRewards;
            if (list == null || list.Count == 0) return;
            bool any = false;
            foreach (var reward in list.ToArray())
            {
                if (!TournamentRules.RewardApplied(Save, reward.week)) continue;
                var r = await PveApi.Claim(reward.week);
                if (string.IsNullOrEmpty(r.error) || r.error == PveErrors.NothingToClaim) { list.Remove(reward); any = true; }
            }
            if (any) GameLogger.Info("[PvE] Cleared already-applied rewards on the server.");
        }

        void MirrorEntry(PveStatusResponse s)
        {
            var save = Save;
            if (save == null || !s.joinedThisWeek || string.IsNullOrEmpty(s.startUtc)) return;
            var t = save.tournament ??= new TournamentSaveData();
            if (s.retired && !t.retired && t.week == s.week && t.startUtc == s.startUtc) MarkRetired(t);
            if (t.week == s.week && t.startUtc == s.startUtc && t.league == s.profile?.league) return;
            bool sameRun = t.week == s.week && t.startUtc == s.startUtc;
            t.week = s.week;
            t.bracketId = s.bracketId;
            t.league = s.profile?.league;
            t.startUtc = s.startUtc;
            t.runEndsUtc = s.runEndsUtc;
            // Restored from the server (another device, a reinstall): this device never prestiged for it, and
            // its net worth is not that run's, so it must not prestige now nor block its snapshots.
            if (!sameRun) { t.prestigeCountAtJoin = -1; t.finalSent = false; }
            SaveManager.Instance.SaveLocal();
        }

        // ── Snapshots ─────────────────────────────────────────────────────

        void Update()
        {
            if (!_subscribed && SaveManager.Instance != null)
            {
                SaveManager.Instance.Backgrounding += OnBackgrounding;
                _subscribed = true;
            }
            _sinceSnapshot += Time.unscaledDeltaTime;
            if (_sending || !Enabled) return;
            var save = Save;
            if (save?.tournament == null || string.IsNullOrEmpty(save.tournament.runEndsUtc)) return;
            if (ECSLoadBridge.Instance == null || !ECSLoadBridge.Instance.IsLoaded || PrestigeRunReset.ReloadPending) return;

            if (TournamentRules.ShouldSnapshot(save.tournament, Now, save.prestigeCount, _sinceSnapshot, _forceSnapshot))
                _ = SendSnapshotAsync();
            else if (Time.unscaledTime >= _pendingRetryAt
                     && TournamentRules.ShouldSendPending(save.tournament, Now, save.prestigeCount))
                _ = SendPendingAsync();
        }

        void OnApplicationPause(bool paused)
        {
            if (!paused) _forceSnapshot = true;   // back from the background: report what offline earnings paid
        }

        // The OS may suspend the app before the send completes, so the snapshot is kept on disk first: if it
        // never lands, the next launch delivers it with its own time (SendPendingAsync).
        void OnBackgrounding()
        {
            var save = Save;
            if (save?.tournament == null || !Enabled) return;
            if (ECSLoadBridge.Instance == null || !ECSLoadBridge.Instance.IsLoaded || PrestigeRunReset.ReloadPending) return;
            var (nw, rate, cap) = SnapshotValues();
            if (TournamentRules.CapturePending(save.tournament, Now, save.prestigeCount, nw, rate, cap))
                SaveManager.Instance.SaveLocal();
            if (!_sending && TournamentRules.ShouldSnapshot(save.tournament, Now, save.prestigeCount, _sinceSnapshot, force: true))
                _ = SendSnapshotAsync();
        }

        /// <summary>
        /// The values a snapshot reports: live net worth; the idle net worth rate (live income x the idle
        /// collection rate, without the timed +50% idle ad boost, which would expire under a projection);
        /// and the idle cap in seconds.
        /// </summary>
        public static (double netWorth, double rate, int cap) SnapshotValues()
        {
            double nw = ECSLoadBridge.Instance?.LiveNetWorth ?? Save?.lastKnownNetWorth ?? 0d;
            var tracker  = FindAnyObjectByType<IncomeRateTracker>();
            var config   = GameBootstrap.Instance != null ? GameBootstrap.Instance.gameConfig : null;
            var upgrades = PersistentUpgradeService.Instance;
            float collect = config != null ? OfflineCollectionService.GetEffectiveCollectionRate(config, upgrades) : 0f;
            float cap     = config != null ? OfflineCollectionService.GetEffectiveIdleCap(config, upgrades) : 0f;
            double rate = (tracker != null ? tracker.PerSecond : 0d) * collect;
            return (nw, Math.Max(0d, rate), Mathf.Max(0, Mathf.RoundToInt(cap)));
        }

        async Task SendSnapshotAsync()
        {
            _sending = true;
            _forceSnapshot = false;
            var save = Save;
            var t = save.tournament;
            bool afterEnd = !TournamentRules.InRun(t, Now);
            var (nw, rate, cap) = SnapshotValues();
            // Keep it until the server confirms it (or a newer one), in case this send never lands.
            TournamentRules.CapturePending(t, Now, save.prestigeCount, nw, rate, cap);
            int carriedT = t.pendingT;     // the kept snapshot this send stands for (-1: none)
            PveSnapshotResponse r;
            try { r = await PveApi.Snapshot(nw, rate, cap); }
            finally { _sending = false; }

            if (string.IsNullOrEmpty(r.error))
            {
                _sinceSnapshot = 0f;
                // Clears the kept copy this send carried, not a newer one captured while it was in flight
                // (going to the background mid-send). Compares device times, not the server's r.t.
                TournamentRules.ClearPendingIfCovered(t, carriedT);
                if (afterEnd) MarkFinalSent();
                GameLogger.Debug($"[PvE] Snapshot t={r.t}s nw={nw:N0} rate={rate:0.##}/s");
            }
            else if (r.error == PveErrors.RunOver || r.error == PveErrors.NotJoined)
            {
                MarkFinalSent();   // nothing more to send for this run
            }
            else if (r.error == PveErrors.Retired)
            {
                MarkRetired(t);    // retired on another device
                OnChanged?.Invoke();
            }
            else if (r.error.StartsWith("network:"))
            {
                _sinceSnapshot = TournamentRules.SnapshotIntervalSeconds - 30f;   // retry in 30 s
            }
            else
            {
                _sinceSnapshot = 0f;
                GameLogger.Warning($"[PvE] Snapshot refused: {r.error} (nw={nw:N0})");
            }
        }

        /// <summary>Delivers the snapshot kept from before the run ended (the app was suspended or offline then).</summary>
        internal async Task SendPendingAsync()
        {
            var t = Save.tournament;
            _sending = true;
            PveSnapshotResponse r;
            try { r = await PveApi.Snapshot(t.pendingNw, t.pendingRate, t.pendingCap, atT: t.pendingT); }
            finally { _sending = false; }

            if (r.error != null && r.error.StartsWith("network:"))
            {
                _pendingRetryAt = Time.unscaledTime + 30f;
                return;
            }
            // Accepted, or refused for good (too late, superseded, settled): either way it is done.
            if (string.IsNullOrEmpty(r.error)) GameLogger.Info($"[PvE] Delivered the kept snapshot from t={t.pendingT}s.");
            else GameLogger.Info($"[PvE] Kept snapshot from t={t.pendingT}s not counted: {r.error}");
            t.pendingT = -1;
            SaveManager.Instance.SaveLocal();
            OnChanged?.Invoke();
        }

        void MarkFinalSent()
        {
            var t = Save?.tournament;
            if (t == null || t.finalSent) return;
            t.finalSent = true;
            SaveManager.Instance.SaveLocal();
            OnChanged?.Invoke();
        }

        /// <summary>Dev console: one line of local state.</summary>
        public string DevStatus()
        {
            var t = Entry;
            string kept = TournamentRules.HasPending(t) ? $"t={t.pendingT}s nw={t.pendingNw:N0}" : "none";
            return $"enabled={Enabled} unlocked={Unlocked} state={PanelState} inRun={InRun} " +
                   $"remaining={TournamentRules.FormatSpan(RunRemaining)} prestigeLeavesRun={PrestigeLeavesRun} retired={t?.retired} " +
                   $"pending={JoinPrestigePending} week={t?.week} bracket={t?.bracketId} final={t?.finalSent} " +
                   $"keptSnapshot={kept} now={PveRules.Iso(Now)}";
        }
    }
}
