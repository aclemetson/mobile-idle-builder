using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Serializes and restores the placed-building grid and conveyor chains.
    /// FlushToSave() is called by SaveManager.SaveLocal() to snapshot ECS → SaveData.
    /// LoadGrid() is called by ECSLoadBridge after ECS singletons are confirmed ready.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class GridSaveService : SingletonMonoBehaviour<GridSaveService>
    {
        protected override bool PersistAcrossScenes => false;

        [SerializeField] BuildingPlacementController placementController;

        EntityManager _em;
        EntityQuery   _buildingQuery;
        EntityQuery   _conveyorQuery;
        bool          _ecsReady;

        protected override void Awake()
        {
            base.Awake();
        }

        void Start()
        {
            if (Instance != this) return;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;

            _em = world.EntityManager;
            _buildingQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingData>(),
                ComponentType.ReadOnly<GridPosition>());
            _conveyorQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<ConveyorSegmentData>());
            _ecsReady = true;
        }

        // ── Clear path ───────────────────────────────────────────────────

        /// <summary>
        /// Destroys all building and conveyor ECS entities, releases grid occupancy,
        /// and removes their visuals. Called by tutorial skip before applying a preset in-place.
        /// </summary>
        public void ClearGrid()
        {
            if (!_ecsReady) return;

            var buildingVisualizer = FindAnyObjectByType<BuildingVisualizer>();
            var conveyorVisualizer  = FindAnyObjectByType<ConveyorVisualizer>();

            var buildingEntities = _buildingQuery.ToEntityArray(Allocator.Temp);
            foreach (var e in buildingEntities)
            {
                var gp = _em.GetComponentData<GridPosition>(e);
                int fw = 1, fh = 1;
                if (_em.HasComponent<BuildingFootprint>(e))
                {
                    var fp = _em.GetComponentData<BuildingFootprint>(e);
                    fw = fp.Width;
                    fh = fp.Height;
                }

                // Preserve entropy sinks (Maxwell's Demon) — they're baked/auto-placed and
                // cannot be recreated through the normal LoadGrid path.
                if (_em.HasComponent<EntropySinkTag>(e))
                {
                    GridOccupancy.Instance?.RegisterRect(gp.Cell.x, gp.Cell.y, fw, fh);
                    continue;
                }

                GridOccupancy.Instance?.ReleaseRect(gp.Cell.x, gp.Cell.y, fw, fh);
                buildingVisualizer?.RemoveBuilding(gp.Cell.x, gp.Cell.y);
                _em.DestroyEntity(e);
            }
            buildingEntities.Dispose();

            var convEntities = _conveyorQuery.ToEntityArray(Allocator.Temp);
            foreach (var e in convEntities)
            {
                var seg = _em.GetComponentData<ConveyorSegmentData>(e);
                GridOccupancy.Instance?.UnregisterConveyor(seg.Cell.x, seg.Cell.y);
                conveyorVisualizer?.RemoveBelt(seg.Cell.x, seg.Cell.y);
                _em.DestroyEntity(e);
            }
            convEntities.Dispose();

            FindAnyObjectByType<FieldGenerator>()?.ClearAllFields();
        }

        /// <summary>
        /// Returns the total entropyCost of all buildings currently in the grid save data,
        /// using the building catalogue from placementController. Used by tutorial skip to
        /// mirror the BaseCurrency→TotalEntropySpent transfer that OnBuildingPlaced would do.
        /// </summary>
        public long ComputeGridBuildingCost()
        {
            var save = SaveManager.Instance?.Current;
            var grid = EnsureActiveGrid(save);
            if (grid?.buildings == null) return 0;
            if (placementController?.availableBuildings == null) return 0;

            var lookup = new Dictionary<int, int>();
            foreach (var entry in placementController.availableBuildings)
                if (entry.building != null)
                    lookup[entry.building.buildingId] = entry.building.entropyCost;

            long total = 0;
            foreach (var bsd in grid.buildings)
                if (lookup.TryGetValue(bsd.buildingId, out int cost))
                    total += cost;

            return total;
        }

        // ── Multi-grid migration ──────────────────────────────────────────

        /// <summary>
        /// Returns the active site's grid, migrating legacy single-grid saves in place.
        /// On a pre-multi-grid save, seeds grids[0] with the SAME object as the legacy
        /// 'grid' so the two alias and stay in sync. Keeps 'grid' mirroring grids[0] while
        /// the origin site (index 0) is active, so an older build can still read the save.
        /// All grid access in this service goes through here. Returns null if no run.
        /// </summary>
        internal static GridSaveData EnsureActiveGrid(SaveData save)
        {
            var run = save?.currentRun;
            if (run == null) return null;

            run.grids ??= new List<GridSaveData>();
            if (run.grids.Count == 0)
                run.grids.Add(run.grid ?? new GridSaveData());

            if (run.activeSiteIndex < 0 || run.activeSiteIndex >= run.grids.Count)
                run.activeSiteIndex = 0;

            var active = run.grids[run.activeSiteIndex];
            if (run.activeSiteIndex == 0)
                run.grid = active;   // legacy mirror

            return active;
        }

        /// <summary>
        /// Ensures <c>run.grids</c> has an entry for <paramref name="siteIndex"/>, padding with
        /// empty grids as needed, and returns that grid. Used when unlocking/switching to a site
        /// whose grid has never been created. grids[0] still aliases the legacy 'grid'.
        /// </summary>
        internal static GridSaveData EnsureSiteGrid(SaveData save, int siteIndex)
        {
            EnsureActiveGrid(save);                 // guarantees grids[0] exists + clamps index
            var run = save?.currentRun;
            if (run == null || siteIndex < 0) return null;

            while (run.grids.Count <= siteIndex)
                run.grids.Add(new GridSaveData());

            return run.grids[siteIndex];
        }

        // ── Save path ─────────────────────────────────────────────────────

        /// <summary>Called by SaveManager.SaveLocal() before writing to disk.</summary>
        public void FlushToSave()
        {
            if (!_ecsReady) return;

            var save = SaveManager.Instance?.Current;
            var grid = EnsureActiveGrid(save);
            if (grid == null) return;

            grid.buildings.Clear();
            grid.conveyors.Clear();

            // --- Buildings ---
            var entities = _buildingQuery.ToEntityArray(Allocator.Temp);
            foreach (var e in entities)
            {
                var bd  = _em.GetComponentData<BuildingData>(e);
                var gp  = _em.GetComponentData<GridPosition>(e);
                var rpd = _em.GetComponentData<RecipeProcessData>(e);

                var bsd = new BuildingSaveData
                {
                    buildingId      = bd.BuildingType,
                    recipeId        = rpd.RecipeID,
                    position        = new[] { gp.Cell.x, gp.Cell.y },
                    level           = bd.UpgradeLevel,
                    storageLevel    = bd.StorageUpgradeLevel,
                    rotation        = 0,
                    flipped         = false,
                    outputDirection = -1
                };

                if (_em.HasComponent<BuildingTransformData>(e))
                {
                    var bt = _em.GetComponentData<BuildingTransformData>(e);
                    bsd.rotation = bt.Rotation;
                    bsd.flipped  = bt.Flipped;
                }

                if (_em.HasComponent<OutputDirectionData>(e))
                    bsd.outputDirection = _em.GetComponentData<OutputDirectionData>(e).Direction;

                grid.buildings.Add(bsd);
            }
            entities.Dispose();

            // --- Conveyors — walk chains from IsChainHead = true ---
            var convEntities = _conveyorQuery.ToEntityArray(Allocator.Temp);
            var segMap = new Dictionary<Entity, ConveyorSegmentData>(convEntities.Length);
            foreach (var e in convEntities)
                segMap[e] = _em.GetComponentData<ConveyorSegmentData>(e);
            convEntities.Dispose();

            foreach (var kv in segMap)
            {
                if (!kv.Value.IsChainHead) continue;

                var cells = new List<int>();
                var cur   = kv.Key;
                int guard = 0;
                while (cur != Entity.Null && segMap.TryGetValue(cur, out var seg) && guard++ < 10000)
                {
                    cells.Add(seg.Cell.x);
                    cells.Add(seg.Cell.y);
                    cur = seg.NextSegment;
                }

                grid.conveyors.Add(new ConveyorSaveData
                {
                    cells = cells.ToArray()
                });
            }

            // --- Fields ---
            grid.fields.Clear();
            foreach (var kv in FieldGenerator.GetAllFields())
                grid.fields.Add(new FieldSaveData
                {
                    fieldId  = kv.Value.id,
                    position = new[] { kv.Key.x, kv.Key.y }
                });

            RebuildIdleSnapshot(save);
        }

        // ── Load path ─────────────────────────────────────────────────────

        /// <summary>Called by ECSLoadBridge after ECS entities are confirmed present.</summary>
        /// <param name="forceApply">If true, bypasses the IsNewGame guard (used by tutorial skip on a fresh session).</param>
        public void LoadGrid(bool forceApply = false)
        {
            var save = SaveManager.Instance?.Current;
            var grid = EnsureActiveGrid(save);
            if (grid == null) return;
            if (!forceApply && SaveManager.Instance.IsNewGame) return;

            bool hasBuildings = grid.buildings?.Count > 0;
            bool hasConveyors = grid.conveyors?.Count > 0;
            bool hasFields    = grid.fields?.Count > 0;
            // Fields must restore even with no buildings/conveyors — a build site can hold a
            // generated field layout that the player has not built on yet.
            if (!hasBuildings && !hasConveyors && !hasFields) return;

            var buildingLookup = new Dictionary<int, BuildingPlacementController.BuildingEntry>();
            if (placementController?.availableBuildings != null)
            {
                foreach (var entry in placementController.availableBuildings)
                {
                    if (entry.building != null)
                        buildingLookup[entry.building.buildingId] = entry;
                }
            }

            var buildingPlacer     = FindAnyObjectByType<BuildingPlacer>();
            var conveyorPlacer     = FindAnyObjectByType<ConveyorPlacer>();
            var buildingVisualizer = FindAnyObjectByType<BuildingVisualizer>();

            // --- Restore buildings ---
            if (hasBuildings && buildingPlacer != null)
            {
                foreach (var bsd in grid.buildings)
                {
                    if (!buildingLookup.TryGetValue(bsd.buildingId, out var entry))
                    {
                        GameLogger.Warning($"[GridSaveService] buildingId {bsd.buildingId} not in availableBuildings — skipped.");
                        continue;
                    }

                    RecipeSO recipe = null;
                    if (bsd.recipeId >= 0 && entry.building.supportedRecipes != null)
                        recipe = Array.Find(entry.building.supportedRecipes,
                                            r => r != null && r.recipeId == bsd.recipeId);
                    recipe ??= entry.defaultRecipe;

                    int? outputDir = bsd.outputDirection >= 0 ? (int?)bsd.outputDirection : null;

                    buildingPlacer.PlaceBuilding(
                        bsd.position[0], bsd.position[1],
                        entry.building, recipe,
                        outputDir, bsd.rotation, bsd.flipped,
                        speedLevel:   Mathf.Max(1, bsd.level),
                        storageLevel: Mathf.Max(1, bsd.storageLevel));
                }
            }

            buildingVisualizer?.Refresh();

            // Re-bake manager bonuses onto the freshly re-placed building entities (the entities are
            // brand new, so any CraftSpeed bonus was lost; OutputQuantity/PowerDiscount components too).
            ManagerService.Instance?.ReapplyAllAssignments();

            // --- Restore conveyors ---
            if (hasConveyors && conveyorPlacer != null)
            {
                foreach (var csd in grid.conveyors)
                {
                    if (csd.cells == null || csd.cells.Length < 4) continue;
                    var path = new List<Vector2Int>();
                    for (int i = 0; i + 1 < csd.cells.Length; i += 2)
                        path.Add(new Vector2Int(csd.cells[i], csd.cells[i + 1]));
                    conveyorPlacer.PlaceConveyorChain(path);
                }
            }

            // --- Restore fields ---
            var fieldGenerator = FindAnyObjectByType<FieldGenerator>();
            if (fieldGenerator != null && grid.fields?.Count > 0)
                fieldGenerator.SpawnFromSave(grid.fields);

            RebuildIdleSnapshot(save);
        }

        // ── Idle snapshot ─────────────────────────────────────────────────────

        /// <summary>
        /// Rebuilds the idle chain snapshot from the current grid save data.
        /// Called at the end of FlushToSave() and LoadGrid() to keep it current.
        /// </summary>
        private void RebuildIdleSnapshot(SaveData save)
        {
            var grid = EnsureActiveGrid(save);
            if (grid == null) return;
            if (placementController?.availableBuildings == null) return;

            // Build lookup: buildingId → BuildingSO and defaultRecipe
            var buildingLookup = new Dictionary<int, BuildingPlacementController.BuildingEntry>();
            foreach (var entry in placementController.availableBuildings)
                if (entry.building != null)
                    buildingLookup[entry.building.buildingId] = entry;

            bool isCollector(int id) =>
                buildingLookup.TryGetValue(id, out var e) &&
                e.building.placementRule == PlacementRule.MustBeOnField;

            bool isEntropySink(int id) =>
                buildingLookup.TryGetValue(id, out var e) && e.building.isEntropySink;

            UnityEngine.Vector2Int getFootprint(int id) =>
                buildingLookup.TryGetValue(id, out var e)
                    ? e.building.footprint
                    : UnityEngine.Vector2Int.one;

            int getOutputItemId(int buildingId, int recipeId)
            {
                if (!buildingLookup.TryGetValue(buildingId, out var entry)) return -1;
                RecipeSO recipe = null;
                if (recipeId >= 0 && entry.building.supportedRecipes != null)
                    recipe = Array.Find(entry.building.supportedRecipes,
                                        r => r != null && r.recipeId == recipeId);
                recipe ??= entry.defaultRecipe;
                return recipe?.outputItem?.itemId ?? -1;
            }

            float getOutputRate(int id) =>
                buildingLookup.TryGetValue(id, out var e) ? e.building.baseOutputRate : 0f;

            float getItemSellValue(int itemId) =>
                ItemDatabase.GetStatic(itemId)?.baseSellValue ?? 0f;

            float boostMult = PremiumShopService.Instance?.GetSpeedBoostMultiplier() ?? 1f;
            // Megastructure global bonuses fold into the idle calc: speed onto the rate multiplier,
            // output onto each source's output multiplier (alongside the per-building manager bonus).
            float megaSpeedMult  = 1f + (MegastructureService.Instance?.GetSpeedBonus()  ?? 0f);
            float megaOutputMult = 1f + (MegastructureService.Instance?.GetOutputBonus() ?? 0f);
            float speedMult = (save.prestigeSpeedMultiplier > 0f ? save.prestigeSpeedMultiplier : 1f) * boostMult * megaSpeedMult;

            int activeSite = save.currentRun?.activeSiteIndex ?? 0;
            float getManagerOutputMultiplier(BuildingSaveData bsd)
            {
                if (bsd?.position == null || bsd.position.Length < 2) return megaOutputMult;
                int posKey = ManagerService.EncodePos(bsd.position[0], bsd.position[1]);
                return (ManagerService.Instance?.GetIdleOutputMultiplierAt(activeSite, posKey) ?? 1f) * megaOutputMult;
            }

            save.idleSnapshot = IdleGraphAnalyzer.BuildSnapshot(
                grid,
                isCollector,
                isEntropySink,
                getFootprint,
                getOutputItemId,
                getOutputRate,
                getItemSellValue,
                speedMult,
                DateTime.UtcNow.ToString("O"),
                getManagerOutputMultiplier);

            // Mirror into the per-site snapshot list so inactive sites keep producing offline.
            // The active site's entry is always kept current here; inactive entries persist from
            // the last time each site was active (rebuilt on switch-away via SaveLocal → FlushToSave).
            MirrorActiveSiteSnapshot(save);

            int builtChains = save.idleSnapshot?.chains?.Count ?? 0;
            GameLogger.Debug($"[Idle] RebuildIdleSnapshot — buildings={grid.buildings?.Count ?? 0} conveyors={grid.conveyors?.Count ?? 0} chains={builtChains}");
        }

        /// <summary>
        /// Copies the freshly built <c>save.idleSnapshot</c> into <c>save.siteSnapshots</c> at the
        /// active site index, padding the list as needed. Keeps the active site's per-site snapshot
        /// in sync so OfflineCollectionService can pay out every unlocked site on the next launch.
        /// </summary>
        internal static void MirrorActiveSiteSnapshot(SaveData save)
        {
            var run = save?.currentRun;
            if (run == null) return;

            int idx = (run.activeSiteIndex >= 0) ? run.activeSiteIndex : 0;
            save.siteSnapshots ??= new List<IdleCollectionSnapshot>();
            while (save.siteSnapshots.Count <= idx)
                save.siteSnapshots.Add(new IdleCollectionSnapshot());

            save.siteSnapshots[idx] = save.idleSnapshot;
        }
    }
}
