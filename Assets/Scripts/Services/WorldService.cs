using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Manages Worlds (tracks) — the layer ABOVE Sites. A World owns a set of member Sites
    /// (<see cref="WorldSO.siteIds"/>, referencing the flat global site list). Unlock is pure
    /// economic: pay entropy + hold the prerequisite research (<see cref="WorldSO.prereqUnlockIds"/>).
    /// Unlocking a World also unlocks its member Sites (so <see cref="SiteService"/> can switch to them);
    /// switching a World delegates the grid handoff to <see cref="SiteService.SwitchTo"/>. World unlocks
    /// survive prestige (<c>save.unlockedWorlds</c>), like site unlocks. world_physics (index 0) is
    /// unlocked from start; its unlock is implicit and never stored.
    /// <para>
    /// Self-bootstrapped (no scene placement, no serialized fields — can't be lost in a scene refactor;
    /// mirrors <c>FeatureFlagService</c>). Persists across scenes; ECS access is grabbed lazily so the
    /// service can be created before GameScene's ECS world exists.
    /// </para>
    /// </summary>
    public class WorldService : SingletonMonoBehaviour<WorldService>
    {
        protected override bool PersistAcrossScenes => true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            new GameObject(nameof(WorldService)).AddComponent<WorldService>();
        }

        /// <summary>Fired after a world is unlocked or the active world changes.</summary>
        public event Action OnWorldsChanged;

        private WorldSO[] _worlds;

        private bool        _ecsReady;
        private EntityQuery _progressQuery;

        public IReadOnlyList<WorldSO> AllWorlds => _worlds;

        public int ActiveIndex => SaveManager.Instance?.Current?.currentRun?.activeWorldIndex ?? 0;

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;
            var db = Resources.Load<WorldDatabaseSO>("WorldDatabase");
            _worlds = db != null ? db.allWorlds : Array.Empty<WorldSO>();
            if (_worlds.Length == 0)
                GameLogger.Warning("[WorldService] WorldDatabase missing or empty — run MobileIdleBuilder/Import Game Data.");
        }

        // ── Query ────────────────────────────────────────────────────────────

        public WorldSO GetWorld(int index) =>
            (_worlds != null && index >= 0 && index < _worlds.Length) ? _worlds[index] : null;

        public int IndexOf(string worldId)
        {
            if (_worlds == null || string.IsNullOrEmpty(worldId)) return -1;
            for (int i = 0; i < _worlds.Length; i++)
                if (_worlds[i] != null && _worlds[i].id == worldId) return i;
            return -1;
        }

        public bool IsUnlocked(int index) =>
            IsUnlocked(SaveManager.Instance?.Current, _worlds, index);

        /// <summary>
        /// True if the world at <paramref name="index"/> is available. Index 0 (physics) is always
        /// unlocked; others must appear in <c>save.unlockedWorlds</c>. Pure/testable.
        /// </summary>
        public static bool IsUnlocked(SaveData save, IReadOnlyList<WorldSO> worlds, int index)
        {
            if (index == 0) return true;                                   // physics implicit
            if (save == null || worlds == null || index < 0 || index >= worlds.Count) return false;
            var world = worlds[index];
            return world != null && save.unlockedWorlds != null && save.unlockedWorlds.Contains(world.id);
        }

        /// <summary>
        /// True if every prerequisite of <paramref name="world"/> is satisfied. Prereqs today are
        /// research ids, so this reads <c>save.unlockedResearch</c>. Pure/testable — no ECS or scene.
        /// </summary>
        public static bool PrereqsMet(SaveData save, WorldSO world)
        {
            if (world == null) return false;
            if (world.prereqUnlockIds == null || world.prereqUnlockIds.Count == 0) return true;
            var unlocked = save?.unlockedResearch;
            foreach (var id in world.prereqUnlockIds)
            {
                if (string.IsNullOrEmpty(id)) continue;
                if (unlocked == null || !unlocked.Contains(id)) return false;
            }
            return true;
        }

        /// <summary>Can the player unlock this world now: not already unlocked, prereqs met, can afford.</summary>
        public bool CanUnlock(int index)
        {
            var world = GetWorld(index);
            if (world == null || IsUnlocked(index)) return false;
            if (!PrereqsMet(SaveManager.Instance?.Current, world)) return false;
            return GetCurrentEntropy() >= world.unlockCost;
        }

        // ── Unlock ───────────────────────────────────────────────────────────

        /// <summary>
        /// Unlocks a world by id: checks prereqs + affordability, deducts entropy, records the id in
        /// <c>save.unlockedWorlds</c> (survives prestige), and unlocks its member sites so the player can
        /// travel there. Returns false if unknown, already unlocked, prereqs unmet, or unaffordable.
        /// </summary>
        public bool UnlockWorld(string worldId)
        {
            int index = IndexOf(worldId);
            var world = GetWorld(index);
            if (world == null || IsUnlocked(index)) return false;

            var save = SaveManager.Instance?.Current;
            if (save == null) return false;
            if (!PrereqsMet(save, world)) return false;
            if (GetCurrentEntropy() < world.unlockCost) return false;

            DeductEntropy(world.unlockCost);

            save.unlockedWorlds ??= new List<string>();
            if (!save.unlockedWorlds.Contains(world.id))
                save.unlockedWorlds.Add(world.id);

            UnlockMemberSites(save, world);   // so SiteService.SwitchTo can reach them

            SaveManager.Instance?.SaveLocal();
            GameLogger.Info($"[WorldService] Unlocked world '{world.id}' for {world.unlockCost}e.");
            OnWorldsChanged?.Invoke();
            return true;
        }

        /// <summary>Adds a world's member sites to <c>unlockedSites</c> and reserves their grid slots.</summary>
        private static void UnlockMemberSites(SaveData save, WorldSO world)
        {
            if (world.siteIds == null) return;
            save.unlockedSites ??= new List<string>();
            var siteSvc = SiteService.Instance;
            foreach (var sid in world.siteIds)
            {
                if (string.IsNullOrEmpty(sid)) continue;
                if (!save.unlockedSites.Contains(sid))
                    save.unlockedSites.Add(sid);
                int sidx = siteSvc != null ? siteSvc.IndexOf(sid) : -1;
                if (sidx >= 0) GridSaveService.EnsureSiteGrid(save, sidx);
            }
        }

        // ── Switch ───────────────────────────────────────────────────────────

        /// <summary>
        /// Travels to the given world's entry site. Delegates the actual grid handoff to
        /// <see cref="SiteService.SwitchTo"/> (which also keeps <c>activeWorldIndex</c> in sync via
        /// <see cref="WorldLayout"/>). No-op if already on that world. False if invalid/locked.
        /// </summary>
        public bool SwitchTo(int index)
        {
            var save = SaveManager.Instance?.Current;
            if (save == null) return false;
            var world = GetWorld(index);
            if (world == null || !IsUnlocked(index)) return false;
            if (index == save.currentRun.activeWorldIndex) return false;

            var siteSvc = SiteService.Instance;
            if (siteSvc == null) return false;

            int siteIndex = EntrySiteIndex(world, siteSvc);
            if (siteIndex < 0) return false;

            // Already physically on that site (world index merely drifted) — just re-sync the world index.
            if (siteIndex == save.currentRun.activeSiteIndex)
            {
                save.currentRun.activeWorldIndex = index;
                SaveManager.Instance?.SaveLocal();
                OnWorldsChanged?.Invoke();
                return true;
            }

            bool ok = siteSvc.SwitchTo(siteIndex);   // updates activeWorldIndex via WorldLayout
            if (ok) OnWorldsChanged?.Invoke();
            return ok;
        }

        /// <summary>Global site index of the world's first resolvable member site, or -1.</summary>
        private static int EntrySiteIndex(WorldSO world, SiteService siteSvc)
        {
            if (siteSvc == null || world?.siteIds == null) return -1;
            foreach (var sid in world.siteIds)
            {
                int i = siteSvc.IndexOf(sid);
                if (i >= 0) return i;
            }
            return -1;
        }

        // ── ECS helpers (lazy — mirror SiteService, but grabbed on first use) ──

        private bool EnsureEcs()
        {
            if (_ecsReady) return true;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return false;
            _progressQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<PlayerProgressData>());
            _ecsReady = true;
            return true;
        }

        private long GetCurrentEntropy()
        {
            if (!EnsureEcs() || _progressQuery.IsEmpty) return 0;
            return _progressQuery.GetSingleton<PlayerProgressData>().BaseCurrency;
        }

        private void DeductEntropy(long amount)
        {
            if (!EnsureEcs() || _progressQuery.IsEmpty) return;
            var progress = _progressQuery.GetSingleton<PlayerProgressData>();
            long actual = Math.Min(amount, progress.BaseCurrency);
            progress.BaseCurrency      -= actual;
            progress.TotalEntropySpent += actual;
            _progressQuery.SetSingleton(progress);
        }
    }
}
