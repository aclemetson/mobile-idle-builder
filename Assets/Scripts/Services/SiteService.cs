using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Manages multiple build sites ("Quantum Domains"). Exactly one site is the live-ECS grid;
    /// inactive sites keep producing via their idle snapshots. Tracks unlock state, deducts entropy
    /// on unlock, and performs the save → destroy → load handoff when switching sites.
    ///
    /// Index in <see cref="AllSites"/> is the canonical site index used by
    /// SaveData.currentRun.activeSiteIndex / grids[] / siteSnapshots[]. site_origin (index 0)
    /// is unlocked from start; its unlock is implicit and never stored in unlockedSites.
    /// </summary>
    public class SiteService : SingletonMonoBehaviour<SiteService>
    {
        protected override bool PersistAcrossScenes => false;

        /// <summary>Fired after a site is unlocked or the active site changes.</summary>
        public event Action OnSitesChanged;

        private SiteSO[] _sites;
        private WorldDatabaseSO _worldDb;   // for keeping activeWorldIndex in sync on switch (see WorldLayout)

        private EntityManager _em;
        private EntityQuery   _progressQuery;

        public IReadOnlyList<SiteSO> AllSites => _sites;

        public int ActiveIndex => SaveManager.Instance?.Current?.currentRun?.activeSiteIndex ?? 0;

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;
            var db = Resources.Load<SiteDatabaseSO>("SiteDatabase");
            _sites = db != null ? db.allSites : Array.Empty<SiteSO>();
            if (_sites.Length == 0)
                GameLogger.Warning("[SiteService] SiteDatabase missing or empty — run MobileIdleBuilder/Import Game Data.");
            _worldDb = Resources.Load<WorldDatabaseSO>("WorldDatabase");
        }

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null)
            {
                _em = world.EntityManager;
                _progressQuery = _em.CreateEntityQuery(ComponentType.ReadWrite<PlayerProgressData>());
            }
        }

        // ── Query ────────────────────────────────────────────────────────────

        public SiteSO GetSite(int index) =>
            (_sites != null && index >= 0 && index < _sites.Length) ? _sites[index] : null;

        public int IndexOf(string siteId)
        {
            if (_sites == null || string.IsNullOrEmpty(siteId)) return -1;
            for (int i = 0; i < _sites.Length; i++)
                if (_sites[i] != null && _sites[i].id == siteId) return i;
            return -1;
        }

        public bool IsUnlocked(int index) =>
            IsUnlocked(SaveManager.Instance?.Current, _sites, index);

        /// <summary>
        /// True if the site at <paramref name="index"/> is available to the player. Index 0
        /// (origin) is always unlocked; others must appear in <c>save.unlockedSites</c>.
        /// Pure helper so the rule is testable without ECS or a live scene.
        /// </summary>
        public static bool IsUnlocked(SaveData save, IReadOnlyList<SiteSO> sites, int index)
        {
            if (index == 0) return true;                                  // origin implicit
            if (save == null || sites == null || index < 0 || index >= sites.Count) return false;
            var site = sites[index];
            return site != null && save.unlockedSites != null && save.unlockedSites.Contains(site.id);
        }

        public bool CanUnlock(int index)
        {
            var site = GetSite(index);
            if (site == null || IsUnlocked(index)) return false;
            return GetCurrentEntropy() >= site.unlockCost;
        }

        // ── Unlock ───────────────────────────────────────────────────────────

        /// <summary>
        /// Unlocks a site by id: deducts its entropy cost, records the id in
        /// <c>save.unlockedSites</c> (survives prestige), and reserves its grid slot.
        /// Returns false if unknown, already unlocked, or unaffordable.
        /// </summary>
        public bool UnlockSite(string siteId)
        {
            int index = IndexOf(siteId);
            var site  = GetSite(index);
            if (site == null) return false;
            if (IsUnlocked(index)) return false;

            var save = SaveManager.Instance?.Current;
            if (save == null) return false;
            if (GetCurrentEntropy() < site.unlockCost) return false;

            DeductEntropy(site.unlockCost);

            save.unlockedSites ??= new List<string>();
            if (!save.unlockedSites.Contains(site.id))
                save.unlockedSites.Add(site.id);

            GridSaveService.EnsureSiteGrid(save, index);   // reserve an (empty) grid for it
            SaveManager.Instance?.SaveLocal();

            GameLogger.Info($"[SiteService] Unlocked site '{site.id}' for {site.unlockCost}e.");
            OnSitesChanged?.Invoke();
            return true;
        }

        // ── Switch ───────────────────────────────────────────────────────────

        /// <summary>
        /// Switches the live grid to the site at <paramref name="index"/>:
        ///   1. SaveLocal() flushes the current site's ECS state → grids[current] + its snapshot.
        ///   2. activeSiteIndex is updated (grid slot reserved).
        ///   3. The current grid's building/conveyor entities are destroyed (fixtures kept).
        ///   4. LoadGrid() rebuilds the target site; first visit regenerates fields per overrides.
        /// Currency and inventory are global and untouched. Returns false if invalid/locked/no-op.
        /// </summary>
        public bool SwitchTo(int index)
        {
            var save = SaveManager.Instance?.Current;
            if (save == null) return false;
            if (_sites == null || index < 0 || index >= _sites.Length) return false;
            if (!IsUnlocked(index)) return false;
            if (index == save.currentRun.activeSiteIndex) return false;

            // 1. Flush the current site → grids[current] (+ rebuild its idle snapshot).
            SaveManager.Instance.SaveLocal();

            // 2. Make the target the active site (reserving its grid slot first). Keep activeWorldIndex
            //    consistent here — SiteService is the single writer of activeSiteIndex, so the owning
            //    world is always resolved from the target site (whether switched via site or world UI).
            var targetGrid = GridSaveService.EnsureSiteGrid(save, index);
            save.currentRun.activeSiteIndex  = index;
            save.currentRun.activeWorldIndex = WorldLayout.WorldIndexForSite(_worldDb, GetSite(index)?.id);

            // 3. Tear down the live grid (buildings + conveyors + occupancy + fields; fixtures kept).
            GridSaveService.Instance?.ClearGrid();

            // 4. Rebuild the target site. LoadGrid restores saved buildings/conveyors/fields.
            GridSaveService.Instance?.LoadGrid(forceApply: true);

            // First visit: no saved fields yet — generate them from the site's field overrides.
            if (targetGrid.fields == null || targetGrid.fields.Count == 0)
                FindAnyObjectByType<FieldGenerator>()?.GenerateForSite(GetSite(index), index);

            // Persist the freshly loaded/generated grid + the new active site's snapshot.
            SaveManager.Instance.SaveLocal();

            GameLogger.Info($"[SiteService] Switched to site [{index}] '{GetSite(index)?.id}'.");
            OnSitesChanged?.Invoke();
            return true;
        }

        // ── ECS helpers (mirror ResearchService) ─────────────────────────────

        private long GetCurrentEntropy()
        {
            if (_progressQuery.IsEmpty) return 0;
            return _progressQuery.GetSingleton<PlayerProgressData>().BaseCurrency;
        }

        private void DeductEntropy(long amount)
        {
            if (_progressQuery.IsEmpty) return;
            var progress = _progressQuery.GetSingleton<PlayerProgressData>();
            long actual = Math.Min(amount, progress.BaseCurrency);
            progress.BaseCurrency      -= actual;
            progress.TotalEntropySpent += actual;
            _progressQuery.SetSingleton(progress);
        }
    }
}
