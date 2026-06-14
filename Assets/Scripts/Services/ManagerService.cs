using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Pure bake/unbake math for a manager bonus on a building. Kept static and ECS-free so the
    /// apply-then-remove exactness (no float drift) is unit-testable without a World.
    /// </summary>
    public static class ManagerBonus
    {
        /// <summary>
        /// Bakes <paramref name="mgr"/>'s bonus into <paramref name="bd"/> and returns the
        /// assignment component to store. CraftSpeed multiplies ProductionSpeed (the pre-bonus
        /// value is captured so it can be restored exactly); OutputQuantity/PowerDiscount are stored
        /// as multipliers and read live by the consuming systems.
        /// </summary>
        public static ManagerAssignmentData Bake(ref BuildingData bd, ManagerSO mgr, int managerIndex)
        {
            var data = new ManagerAssignmentData
            {
                ManagerIndex      = managerIndex,
                PreBonusSpeed     = bd.ProductionSpeed,
                AppliedOutputMult = 1f,
                AppliedPowerMult  = 1f,
            };

            switch (mgr.bonusType)
            {
                case ManagerBonusType.CraftSpeed:     bd.ProductionSpeed *= mgr.bonusValue; break;
                case ManagerBonusType.OutputQuantity: data.AppliedOutputMult = mgr.bonusValue; break;
                case ManagerBonusType.PowerDiscount:  data.AppliedPowerMult  = mgr.bonusValue; break;
            }
            return data;
        }

        /// <summary>Restores the building's pre-bonus speed exactly (no division).</summary>
        public static void Unbake(ref BuildingData bd, ManagerAssignmentData data)
        {
            bd.ProductionSpeed = data.PreBonusSpeed;
        }
    }

    /// <summary>
    /// Manages hireable building managers: tracks the catalogue, the hired roster, and per-building
    /// assignments, and bakes/un-bakes the bonuses on the building ECS entities.
    /// <para>
    /// Hired managers and assignments survive prestige as far as the manager (buildings are destroyed
    /// on prestige, so <see cref="ResetAssignments"/> clears the building links but keeps the roster).
    /// One manager per building and one building per manager are both enforced.
    /// </para>
    /// </summary>
    public class ManagerService : SingletonMonoBehaviour<ManagerService>
    {
        protected override bool PersistAcrossScenes => false;

        /// <summary>Fired whenever the hired roster or any assignment changes (for UI refresh).</summary>
        public event Action OnChanged;

        private ManagerSO[] _all = Array.Empty<ManagerSO>();

        // ── Runtime state (mirrored to SaveData) ──────────────────────────────
        private readonly HashSet<string> _hired = new();
        private readonly Dictionary<string, ManagerAssignmentEntry> _assignByManager = new();
        private readonly Dictionary<long, string> _managerByBuilding = new(); // BKey(site,pos) -> managerId

        // ── ECS ───────────────────────────────────────────────────────────────
        private EntityManager _em;
        private EntityQuery   _prestigeQuery;
        private EntityQuery   _buildingQuery;
        private bool          _emReady;
        private bool          _loaded;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;
            var db = Resources.Load<ManagerDatabaseSO>("ManagerDatabase");
            _all = db != null ? db.allManagers : Array.Empty<ManagerSO>();
        }

        void Start()
        {
            EnsureEcs();
            LoadFromSave();
        }

        private void EnsureEcs()
        {
            if (_emReady) return;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;
            _em            = world.EntityManager;
            _prestigeQuery = _em.CreateEntityQuery(ComponentType.ReadWrite<PrestigeData>());
            _buildingQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingData>(), ComponentType.ReadOnly<GridPosition>());
            _emReady = true;
        }

        // ── Catalogue ─────────────────────────────────────────────────────────

        public IReadOnlyList<ManagerSO> AllManagers => _all;
        public int ManagerCount => _all.Length;
        public ManagerSO GetManager(int index) =>
            (index >= 0 && index < _all.Length) ? _all[index] : null;

        public int IndexOf(string managerId)
        {
            for (int i = 0; i < _all.Length; i++)
                if (_all[i] != null && _all[i].id == managerId) return i;
            return -1;
        }

        public ManagerSO Find(string managerId)
        {
            int i = IndexOf(managerId);
            return i >= 0 ? _all[i] : null;
        }

        // ── Hiring ────────────────────────────────────────────────────────────

        public bool IsHired(string managerId) => _hired.Contains(managerId);

        /// <summary>Held prestige currency (✦), or 0 if ECS is not ready.</summary>
        public long HeldPrestige()
        {
            EnsureEcs();
            if (!_emReady || _prestigeQuery.IsEmpty) return 0L;
            var p = _em.GetComponentData<PrestigeData>(_prestigeQuery.GetSingletonEntity());
            return p.PrestigeCurrency - p.PrestigeCurrencySpent;
        }

        /// <summary>Hires a manager, deducting its prestige-currency cost. Returns false when already
        /// hired, unknown, ECS not ready, or unaffordable.</summary>
        public bool Hire(string managerId)
        {
            if (string.IsNullOrEmpty(managerId) || IsHired(managerId)) return false;
            var mgr = Find(managerId);
            if (mgr == null) return false;
            if (!TryDeductPrestige(mgr.hireCostPrestige)) return false;

            _hired.Add(managerId);
            FlushToSave();
            SaveManager.Instance?.SaveLocal();   // persists the deducted PrestigeCurrencySpent + roster
            OnChanged?.Invoke();
            return true;
        }

        private bool TryDeductPrestige(int cost)
        {
            EnsureEcs();
            if (!_emReady || _prestigeQuery.IsEmpty) return false;
            var e = _prestigeQuery.GetSingletonEntity();
            var p = _em.GetComponentData<PrestigeData>(e);
            long held = p.PrestigeCurrency - p.PrestigeCurrencySpent;
            if (held < cost) return false;
            p.PrestigeCurrencySpent += cost;
            _em.SetComponentData(e, p);
            return true;
        }

        // ── Assignment ────────────────────────────────────────────────────────

        public bool IsAssigned(string managerId) => _assignByManager.ContainsKey(managerId);

        public ManagerAssignmentEntry GetAssignment(string managerId) =>
            _assignByManager.TryGetValue(managerId, out var e) ? e : null;

        /// <summary>The manager assigned to the building at (siteIndex, posKey), or null.</summary>
        public ManagerSO GetManagerAtBuilding(int siteIndex, int posKey) =>
            _managerByBuilding.TryGetValue(BKey(siteIndex, posKey), out var id) ? Find(id) : null;

        /// <summary>
        /// The offline-throughput multiplier for the building at (siteIndex, posKey): an
        /// OutputQuantity manager's value, else 1. CraftSpeed/PowerDiscount do not affect a
        /// collector's idle rate (collectors are fixed-rate and ignore ProductionSpeed live).
        /// </summary>
        public float GetIdleOutputMultiplierAt(int siteIndex, int posKey)
        {
            if (!_loaded) LoadFromSave();
            var mgr = GetManagerAtBuilding(siteIndex, posKey);
            return (mgr != null && mgr.bonusType == ManagerBonusType.OutputQuantity)
                ? mgr.bonusValue : 1f;
        }

        /// <summary>
        /// Assigns a hired manager to the building at (siteIndex, posKey). Enforces one manager per
        /// building (any existing manager on that building is unassigned) and one building per manager
        /// (the manager is moved off any previous building). Applies the bonus to the live entity when
        /// the building is on the active site.
        /// </summary>
        public bool Assign(string managerId, int siteIndex, int posKey)
        {
            if (!IsHired(managerId)) return false;

            long bkey = BKey(siteIndex, posKey);

            // One manager per building: drop whatever manager currently sits on this building.
            if (_managerByBuilding.TryGetValue(bkey, out var existing) && existing != managerId)
                UnassignInternal(existing);

            // One building per manager: move this manager off its previous building.
            if (_assignByManager.ContainsKey(managerId))
                UnassignInternal(managerId);

            var entry = new ManagerAssignmentEntry
            {
                managerId      = managerId,
                siteIndex      = siteIndex,
                buildingPosKey = posKey,
            };
            _assignByManager[managerId] = entry;
            _managerByBuilding[bkey]    = managerId;

            if (siteIndex == ActiveSiteIndex && TryFindBuilding(posKey, out var e))
                ApplyBonusTo(e, IndexOf(managerId));

            FlushToSave();
            SaveManager.Instance?.SaveLocal();
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>Removes a manager from its building (keeps it hired).</summary>
        public void Unassign(string managerId)
        {
            if (!_assignByManager.ContainsKey(managerId)) return;
            UnassignInternal(managerId);
            FlushToSave();
            SaveManager.Instance?.SaveLocal();
            OnChanged?.Invoke();
        }

        private void UnassignInternal(string managerId)
        {
            if (!_assignByManager.TryGetValue(managerId, out var entry)) return;
            _assignByManager.Remove(managerId);
            _managerByBuilding.Remove(BKey(entry.siteIndex, entry.buildingPosKey));

            if (entry.siteIndex == ActiveSiteIndex && TryFindBuilding(entry.buildingPosKey, out var e))
                RemoveBonusFrom(e);
        }

        // ── ECS bake/unbake ───────────────────────────────────────────────────

        /// <summary>Bakes manager <paramref name="managerIndex"/>'s bonus into the building entity,
        /// using the entity's CURRENT ProductionSpeed as the pre-bonus base.</summary>
        public void ApplyBonusTo(Entity building, int managerIndex)
        {
            EnsureEcs();
            if (!_emReady || managerIndex < 0) return;
            if (!_em.Exists(building) || !_em.HasComponent<BuildingData>(building)) return;
            var mgr = GetManager(managerIndex);
            if (mgr == null) return;

            var bd   = _em.GetComponentData<BuildingData>(building);
            var data = ManagerBonus.Bake(ref bd, mgr, managerIndex);
            _em.SetComponentData(building, bd);

            if (_em.HasComponent<ManagerAssignmentData>(building))
                _em.SetComponentData(building, data);
            else
                _em.AddComponentData(building, data);
        }

        /// <summary>Restores the building's pre-bonus state and removes the assignment component.</summary>
        public void RemoveBonusFrom(Entity building)
        {
            EnsureEcs();
            if (!_emReady || !_em.Exists(building)) return;
            if (!_em.HasComponent<ManagerAssignmentData>(building)) return;

            var data = _em.GetComponentData<ManagerAssignmentData>(building);
            if (_em.HasComponent<BuildingData>(building))
            {
                var bd = _em.GetComponentData<BuildingData>(building);
                ManagerBonus.Unbake(ref bd, data);
                _em.SetComponentData(building, bd);
            }
            _em.RemoveComponent<ManagerAssignmentData>(building);
        }

        /// <summary>
        /// Re-applies the assignment after a building's ProductionSpeed was reset from level (e.g. a
        /// speed upgrade overwrote it). Reads the manager from the existing component and re-bakes
        /// using the just-reset speed as the new base. Call this AFTER writing the level-based speed.
        /// </summary>
        public void ReapplyAfterSpeedReset(Entity building)
        {
            EnsureEcs();
            if (!_emReady || !_em.Exists(building)) return;
            if (!_em.HasComponent<ManagerAssignmentData>(building)) return;
            var data = _em.GetComponentData<ManagerAssignmentData>(building);
            ApplyBonusTo(building, data.ManagerIndex);
        }

        /// <summary>
        /// Applies every assignment for the active site onto the freshly re-placed building entities.
        /// Called by GridSaveService after LoadGrid (initial load and site switch). Skips buildings
        /// that already carry an assignment component to avoid double-baking.
        /// </summary>
        public void ReapplyAllAssignments()
        {
            EnsureEcs();
            if (!_loaded) LoadFromSave();
            if (!_emReady) return;

            int active = ActiveSiteIndex;
            foreach (var entry in _assignByManager.Values)
            {
                if (entry.siteIndex != active) continue;
                if (!TryFindBuilding(entry.buildingPosKey, out var e)) continue;
                if (_em.HasComponent<ManagerAssignmentData>(e)) continue;
                ApplyBonusTo(e, IndexOf(entry.managerId));
            }
        }

        /// <summary>Clears all building assignments (keeps the hired roster). Called on prestige —
        /// the building entities are already destroyed, so only the maps + save list are cleared.</summary>
        public void ResetAssignments()
        {
            _assignByManager.Clear();
            _managerByBuilding.Clear();
            var save = SaveManager.Instance?.Current;
            save?.managerAssignments?.Clear();
            OnChanged?.Invoke();
        }

        // ── Save / load ───────────────────────────────────────────────────────

        public void LoadFromSave()
        {
            _hired.Clear();
            _assignByManager.Clear();
            _managerByBuilding.Clear();
            _loaded = true;

            var save = SaveManager.Instance?.Current;
            if (save == null) return;

            if (save.hiredManagers != null)
                foreach (var id in save.hiredManagers)
                    if (!string.IsNullOrEmpty(id)) _hired.Add(id);

            if (save.managerAssignments != null)
                foreach (var entry in save.managerAssignments)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.managerId)) continue;
                    if (!_hired.Contains(entry.managerId)) continue; // drop orphaned assignment
                    _assignByManager[entry.managerId] = entry;
                    _managerByBuilding[BKey(entry.siteIndex, entry.buildingPosKey)] = entry.managerId;
                }
        }

        public void FlushToSave()
        {
            var save = SaveManager.Instance?.Current;
            if (save == null) return;

            save.hiredManagers ??= new List<string>();
            save.hiredManagers.Clear();
            foreach (var id in _hired) save.hiredManagers.Add(id);

            save.managerAssignments ??= new List<ManagerAssignmentEntry>();
            save.managerAssignments.Clear();
            foreach (var entry in _assignByManager.Values) save.managerAssignments.Add(entry);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private int ActiveSiteIndex =>
            SaveManager.Instance?.Current?.currentRun?.activeSiteIndex ?? 0;

        /// <summary>Encodes a grid anchor cell as a single int key (matches BuildingSaveData positions).</summary>
        public static int EncodePos(int x, int y) => x * 10000 + y;

        private static long BKey(int siteIndex, int posKey) =>
            ((long)siteIndex << 32) | (uint)posKey;

        /// <summary>Finds the live building entity whose anchor cell encodes to <paramref name="posKey"/>.</summary>
        public bool TryFindBuilding(int posKey, out Entity result)
        {
            result = Entity.Null;
            EnsureEcs();
            if (!_emReady || _buildingQuery.IsEmpty) return false;

            var entities = _buildingQuery.ToEntityArray(Allocator.Temp);
            try
            {
                foreach (var e in entities)
                {
                    var cell = _em.GetComponentData<GridPosition>(e).Cell;
                    if (EncodePos(cell.x, cell.y) == posKey) { result = e; return true; }
                }
            }
            finally { entities.Dispose(); }
            return false;
        }
    }
}
