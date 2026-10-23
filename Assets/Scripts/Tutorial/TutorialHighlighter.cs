using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Reusable tutorial highlight system.
    ///
    /// ShowHighlight(id)  — pan camera to target + amber tile + pulsing quad
    /// ShowVisualOnly(id) — amber tile + pulsing quad only, camera stays on player
    /// ClearHighlight()   — return camera to character + remove all visuals
    ///
    /// Because field positions are randomised at runtime by FieldGenerator (a coroutine),
    /// ShowHighlight starts a background search that retries every frame until the field
    /// exists in the scene, then applies the highlight. This makes it safe to call at any
    /// point during startup without worrying about spawn order.
    ///
    /// Supported target IDs:
    ///   FieldInstance.Field.id — e.g. "electron_field", "quark_field"
    ///   "maxwells_demon", "atomic_assembler" — placed buildings
    ///   "route:&lt;from&gt;&gt;&lt;to&gt;" — the start and end tiles of the next belt to draw: the free
    ///       cell in front of a &lt;from&gt; output and the free cell in front of a &lt;to&gt; input (#202)
    ///   "suggest:&lt;building&gt;:&lt;from&gt;&gt;&lt;to&gt;" — where to place &lt;building&gt; so belts from
    ///       &lt;from&gt; feed it and its output reaches &lt;to&gt; (#203)
    ///   "power:&lt;building&gt;" — the nearest free spot where &lt;building&gt; is inside a placed generator's
    ///       influence circle (PowerCoverageMath), so it gets power
    ///   Building keys are BuildingSO ids (e.g. "harvester", "strong_force_combiner");
    ///   "maxwells_demon" matches the entropy sink. Grid math lives in TutorialRouteGuide.
    ///
    /// Inspector wiring required:
    ///   cameraFollow  → CameraController on Main Camera
    ///   gridRenderer  → GridRenderer in scene
    /// </summary>
    public class TutorialHighlighter : MonoBehaviour
    {
        [SerializeField] private CameraController cameraFollow;
        [SerializeField] private GridRenderer     gridRenderer;
        [SerializeField] private Material         _highlightMaterial;  // URP Unlit Transparent — assign in Inspector

        private readonly List<TutorialTileHighlight> _activeHighlights = new();
        private Coroutine             _searchRoutine;

        /// <summary>Everything ApplyHighlight needs: the anchor grid cell, footprint size, and camera target.</summary>
        private struct WorldTarget
        {
            public int     AnchorX;
            public int     AnchorZ;
            public int     FootprintW;
            public int     FootprintH;
            public Vector3 CameraTarget; // world-space point to pan the camera to
            public bool    HasSecond;    // route targets: a second 1x1 tile (the belt's end)
            public int     SecondX;
            public int     SecondZ;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Pan camera to the named target and show the amber tile + pulse.</summary>
        public void ShowHighlight(string targetId)  => BeginSearch(targetId, panCamera: true);

        /// <summary>Show the amber tile + pulse without moving the camera.</summary>
        public void ShowVisualOnly(string targetId) => BeginSearch(targetId, panCamera: false);

        /// <summary>Return camera to the character and remove all highlight visuals.</summary>
        public void ClearHighlight()
        {
            if (_searchRoutine != null)
            {
                StopCoroutine(_searchRoutine);
                _searchRoutine = null;
            }

            cameraFollow?.ResumeFollow();
            gridRenderer?.ClearTutorialHighlightCell();
            ClearVisual();
        }

        // ── Internals ─────────────────────────────────────────────────────────

        private void BeginSearch(string targetId, bool panCamera)
        {
            if (string.IsNullOrEmpty(targetId)) return;

            // Cancel any in-progress search for a previous target
            if (_searchRoutine != null)
            {
                StopCoroutine(_searchRoutine);
                _searchRoutine = null;
            }

            _searchRoutine = StartCoroutine(SearchAndHighlight(targetId, panCamera));
        }

        /// <summary>
        /// Retries every frame until FieldGenerator has spawned the target,
        /// then applies the camera pan and/or visual highlight.
        /// Times out after 15 seconds to avoid an infinite loop on bad IDs.
        /// </summary>
        private IEnumerator SearchAndHighlight(string targetId, bool panCamera)
        {
            float        timeout = 15f;
            WorldTarget? target  = null;

            while (target == null)
            {
                target   = FindWorldTarget(targetId);
                timeout -= Time.deltaTime;

                if (timeout <= 0f)
                {
                    GameLogger.Warning($"[TutorialHighlighter] Timed out searching for target '{targetId}'. " +
                                     "Check that the FieldSO id matches exactly.");
                    _searchRoutine = null;
                    yield break;
                }

                if (target == null)
                    yield return null;
            }

            _searchRoutine = null;
            ApplyHighlight(target.Value, panCamera);
        }

        private void ApplyHighlight(WorldTarget t, bool panCamera)
        {
            if (panCamera)
                cameraFollow?.PanTo(t.CameraTarget);

            gridRenderer?.SetTutorialHighlightRect(t.AnchorX, t.AnchorZ, t.FootprintW, t.FootprintH);

            float cs = gridRenderer != null ? gridRenderer.CellSize : 1f;
            ClearVisual();

            var firstCenter = new Vector3((t.AnchorX + (t.FootprintW - 1) * 0.5f) * cs, 0f,
                                          (t.AnchorZ + (t.FootprintH - 1) * 0.5f) * cs);
            _activeHighlights.Add(TutorialTileHighlight.Spawn(firstCenter, cs, t.FootprintW, t.FootprintH, _highlightMaterial));

            if (t.HasSecond)
            {
                gridRenderer?.AddTutorialHighlightRect(t.SecondX, t.SecondZ);
                _activeHighlights.Add(TutorialTileHighlight.Spawn(
                    new Vector3(t.SecondX * cs, 0f, t.SecondZ * cs), cs, 1, 1, _highlightMaterial));
            }
        }

        private WorldTarget? FindWorldTarget(string targetId)
        {
            // Scene-based field instances
            var fields = FindObjectsByType<FieldInstance>(FindObjectsSortMode.None);
            foreach (var f in fields)
            {
                if (f.Field == null || f.Field.id != targetId) continue;

                float cs = gridRenderer != null ? gridRenderer.CellSize : 1f;
                int   ax = Mathf.RoundToInt(f.transform.position.x / cs);
                int   az = Mathf.RoundToInt(f.transform.position.z / cs);
                // Camera target = tile centre on the ground plane
                var camTarget = new Vector3(ax * cs, 0f, az * cs);
                return new WorldTarget { AnchorX = ax, AnchorZ = az,
                                         FootprintW = 1, FootprintH = 1,
                                         CameraTarget = camTarget };
            }

            // ECS buildings
            if (targetId == "maxwells_demon")
                return FindEntropySinkTarget();

            if (targetId == "atomic_assembler")
                return FindPlacedBuildingTarget(4);

            if (TutorialRouteGuide.TryParseRoute(targetId, out var from, out var to))
                return FindRouteTarget(from, to);

            if (TutorialRouteGuide.TryParseSuggest(targetId, out var building, out var sFrom, out var sTo))
                return FindSuggestTarget(building, sFrom, sTo);

            if (TutorialRouteGuide.TryParsePower(targetId, out var powered))
                return FindPoweredTarget(powered);

            return null;
        }

        // ── Power placement guidance ──────────────────────────────────────────

        /// <summary>
        /// Nearest free spot where <paramref name="buildingKey"/> is inside a placed power source's influence
        /// circle, so the grid links it. Retries until a generator is placed (returns null until then).
        /// </summary>
        private WorldTarget? FindPoweredTarget(string buildingKey)
        {
            var so = FindBuildingSO(buildingKey);
            if (so == null || gridRenderer == null) return null;

            var sources = GatherPowerSources();
            if (sources.Count == 0) return null;

            var fp = new Vector2Int(Mathf.Max(1, so.footprint.x), Mathf.Max(1, so.footprint.y));
            var a  = TutorialRouteGuide.SuggestPoweredAnchor(gridRenderer.Width, gridRenderer.Height, fp,
                sources, IsBuildable);
            if (a == null) return null;

            var anchor = a.Value;
            float cs = CellSize;
            return new WorldTarget
            {
                AnchorX = anchor.x, AnchorZ = anchor.y, FootprintW = fp.x, FootprintH = fp.y,
                CameraTarget = new Vector3((anchor.x + (fp.x - 1) * 0.5f) * cs, 0f, (anchor.y + (fp.y - 1) * 0.5f) * cs)
            };
        }

        /// <summary>Footprint and influence radius of every placed generator (a power node with MaxEV above zero).</summary>
        private static List<(Vector2Int min, Vector2Int max, float radius)> GatherPowerSources()
        {
            var sources = new List<(Vector2Int, Vector2Int, float)>();
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return sources;
            var em = world.EntityManager;

            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<PowerNodeData>(),
                                                   ComponentType.ReadOnly<GridPosition>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            foreach (var entity in entities)
            {
                var node = em.GetComponentData<PowerNodeData>(entity);
                if (node.MaxEV <= 0f) continue;

                var  pos = em.GetComponentData<GridPosition>(entity).Cell;
                int  w = 1, h = 1;
                if (em.HasComponent<BuildingFootprint>(entity))
                {
                    var fp = em.GetComponentData<BuildingFootprint>(entity);
                    w = fp.Width;
                    h = fp.Height;
                }
                sources.Add((new Vector2Int(pos.x, pos.y),
                             new Vector2Int(pos.x + w - 1, pos.y + h - 1),
                             node.InfluenceRadius));
            }
            return sources;
        }

        // ── Route / placement guidance (#202, #203) ───────────────────────────

        /// <summary>Start tile = free cell in front of a &lt;from&gt; output; end = free cell in front of a &lt;to&gt; input.</summary>
        private WorldTarget? FindRouteTarget(string from, string to)
        {
            var starts = new List<Vector2Int>();
            var ends   = new List<Vector2Int>();
            if (!GatherPortFronts(from, PortType.Output, starts) || !GatherPortFronts(to, PortType.Input, ends))
                return null;

            if (!TutorialRouteGuide.TryPickRoute(starts, ends, IsBeltFree, out var s, out var e))
                return null;

            float cs = CellSize;
            return new WorldTarget
            {
                AnchorX = s.x, AnchorZ = s.y, FootprintW = 1, FootprintH = 1,
                HasSecond = true, SecondX = e.x, SecondZ = e.y,
                CameraTarget = new Vector3((s.x + e.x) * 0.5f * cs, 0f, (s.y + e.y) * 0.5f * cs)
            };
        }

        /// <summary>Footprint rect where &lt;building&gt; fits best between &lt;from&gt; outputs and &lt;to&gt; inputs.</summary>
        private WorldTarget? FindSuggestTarget(string buildingKey, string from, string to)
        {
            var so = FindBuildingSO(buildingKey);
            if (so == null || gridRenderer == null) return null;

            var sources = new List<Vector2Int>();
            var sinks   = new List<Vector2Int>();
            // Only sources making something this building uses: the electron Harvester is a "harvester"
            // too, and counting it as a third Combiner source pulled the glow a cell off (#236).
            if (!GatherPortFronts(from, PortType.Output, sources, ConsumedItemIds(so)) ||
                !GatherPortFronts(to, PortType.Input, sinks))
                return null;
            sources.RemoveAll(c => !IsBeltFree(c)); // already-routed outputs are not looking for a destination
            sinks.RemoveAll(c => !IsBeltFree(c));

            var fp           = new Vector2Int(Mathf.Max(1, so.footprint.x), Mathf.Max(1, so.footprint.y));
            var inputFronts  = new List<Vector2Int>();
            var outputFronts = new List<Vector2Int>();
            if (so.ports != null)
            {
                foreach (var port in so.ports)
                {
                    var (cell, facing) = PortUtils.TransformPort(port, fp, flipped: false, rotation: 0);
                    if (port.portType == PortType.Input)       inputFronts.Add(TutorialRouteGuide.InputFront(cell, facing));
                    else if (port.portType == PortType.Output) outputFronts.Add(TutorialRouteGuide.OutputFront(cell, facing));
                }
            }

            var anchor = TutorialRouteGuide.SuggestAnchor(gridRenderer.Width, gridRenderer.Height, fp,
                inputFronts, outputFronts, sources, sinks, IsBuildable, IsBeltFree);
            if (anchor == null) return null;

            var a = anchor.Value;
            float cs = CellSize;
            return new WorldTarget
            {
                AnchorX = a.x, AnchorZ = a.y, FootprintW = fp.x, FootprintH = fp.y,
                CameraTarget = new Vector3((a.x + (fp.x - 1) * 0.5f) * cs, 0f, (a.y + (fp.y - 1) * 0.5f) * cs)
            };
        }

        /// <summary>
        /// Adds the belt cell in front of every <paramref name="type"/> port on placed buildings matching
        /// <paramref name="buildingKey"/>. Returns false when no such building is placed yet (keep searching).
        /// </summary>
        private bool GatherPortFronts(string buildingKey, PortType type, List<Vector2Int> result,
                                      HashSet<int> onlyProducing = null)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return false;
            var em = world.EntityManager;

            bool isDemon = buildingKey == "maxwells_demon";
            int  typeId  = -1;
            if (!isDemon)
            {
                var so = FindBuildingSO(buildingKey);
                if (so == null) return false;
                typeId = so.buildingId;
            }

            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<BuildingData>(),
                                                   ComponentType.ReadOnly<PlacedPortData>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            bool any = false;
            foreach (var entity in entities)
            {
                bool match = isDemon
                    ? em.HasComponent<EntropySinkTag>(entity)
                    : em.GetComponentData<BuildingData>(entity).BuildingType == typeId;
                if (!match) continue;
                any = true;
                if (onlyProducing != null && onlyProducing.Count > 0 &&
                    ProducedItemId(em, entity) is int produced && !onlyProducing.Contains(produced))
                    continue;

                var ports = em.GetBuffer<PlacedPortData>(entity, isReadOnly: true);
                for (int i = 0; i < ports.Length; i++)
                {
                    if (ports[i].PortType != (int)type) continue;
                    var cell   = new Vector2Int(ports[i].CellX, ports[i].CellY);
                    var facing = (OutputDirection)ports[i].Facing;
                    result.Add(type == PortType.Output
                        ? TutorialRouteGuide.OutputFront(cell, facing)
                        : TutorialRouteGuide.InputFront(cell, facing));
                }
            }
            return any;
        }

        /// <summary>Item ids consumed by any of <paramref name="building"/>'s recipes.</summary>
        private static HashSet<int> ConsumedItemIds(BuildingSO building)
        {
            var ids = new HashSet<int>();
            if (building?.supportedRecipes == null) return ids;
            foreach (var r in building.supportedRecipes)
            {
                if (r?.inputs == null) continue;
                foreach (var input in r.inputs)
                    if (input.item != null) ids.Add(input.item.itemId);
            }
            return ids;
        }

        /// <summary>The item a placed building's current recipe makes, or null when it has none or it is unknown.</summary>
        private static int? ProducedItemId(EntityManager em, Entity entity)
        {
            if (!em.HasComponent<RecipeProcessData>(entity) || !em.HasComponent<BuildingData>(entity)) return null;
            int recipeId = em.GetComponentData<RecipeProcessData>(entity).RecipeID;
            var so = FindBuildingSO(em.GetComponentData<BuildingData>(entity).BuildingType);
            if (so?.supportedRecipes == null) return null;
            foreach (var r in so.supportedRecipes)
                if (r != null && r.recipeId == recipeId && r.outputItem != null) return r.outputItem.itemId;
            return null;
        }

        private static BuildingSO FindBuildingSO(int buildingId)
        {
            var db = Resources.Load<BuildingDatabaseSO>("BuildingDatabase");
            if (db == null || db.allBuildings == null) return null;
            foreach (var b in db.allBuildings)
                if (b != null && b.buildingId == buildingId) return b;
            return null;
        }

        private static BuildingSO FindBuildingSO(string id)
        {
            var db = Resources.Load<BuildingDatabaseSO>("BuildingDatabase");
            if (db == null || db.allBuildings == null) return null;
            foreach (var b in db.allBuildings)
                if (b != null && b.id == id) return b;
            return null;
        }

        private float CellSize => gridRenderer != null ? gridRenderer.CellSize : 1f;

        private bool IsBeltFree(Vector2Int c) =>
            gridRenderer != null && gridRenderer.IsInBounds(c.x, c.y)
            && (GridOccupancy.Instance == null || !GridOccupancy.Instance.IsOccupied(c.x, c.y));

        private bool IsBuildable(Vector2Int c) => IsBeltFree(c) && FieldGenerator.GetFieldAt(c.x, c.y) == null;

        /// <summary>
        /// Returns the highlight target for the first entity with EntropySinkTag, including
        /// its full footprint from BuildingFootprint (defaults to 1×1 if absent).
        /// </summary>
        private WorldTarget? FindEntropySinkTarget()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return null;

            var em = world.EntityManager;
            using var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<EntropySinkTag>(),
                ComponentType.ReadOnly<GridPosition>()
            );

            if (query.IsEmpty) return null;

            using var entities = query.ToEntityArray(Allocator.Temp);
            var entity = entities[0];
            var pos    = em.GetComponentData<GridPosition>(entity);
            float cs   = gridRenderer != null ? gridRenderer.CellSize : 1f;

            int w = 1, h = 1;
            if (em.HasComponent<BuildingFootprint>(entity))
            {
                var fp = em.GetComponentData<BuildingFootprint>(entity);
                w = fp.Width;
                h = fp.Height;
            }

            // Camera target = world centre of the full footprint
            var camTarget = new Vector3(
                (pos.Cell.x + (w - 1) * 0.5f) * cs, 0f,
                (pos.Cell.y + (h - 1) * 0.5f) * cs);

            return new WorldTarget
            {
                AnchorX      = pos.Cell.x,
                AnchorZ      = pos.Cell.y,
                FootprintW   = w,
                FootprintH   = h,
                CameraTarget = camTarget
            };
        }

        /// <summary>
        /// Returns the highlight target for the first placed building with the given BuildingType integer.
        /// </summary>
        private WorldTarget? FindPlacedBuildingTarget(int buildingType)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return null;

            var em = world.EntityManager;
            using var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingData>(),
                ComponentType.ReadOnly<GridPosition>()
            );

            if (query.IsEmpty) return null;

            using var entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                var bd = em.GetComponentData<BuildingData>(entity);
                if (bd.BuildingType != buildingType) continue;

                var pos  = em.GetComponentData<GridPosition>(entity);
                float cs = gridRenderer != null ? gridRenderer.CellSize : 1f;
                int w = 1, h = 1;
                if (em.HasComponent<BuildingFootprint>(entity))
                {
                    var fp = em.GetComponentData<BuildingFootprint>(entity);
                    w = fp.Width; h = fp.Height;
                }
                var camTarget = new Vector3(
                    (pos.Cell.x + (w - 1) * 0.5f) * cs, 0f,
                    (pos.Cell.y + (h - 1) * 0.5f) * cs);
                return new WorldTarget
                {
                    AnchorX    = pos.Cell.x, AnchorZ      = pos.Cell.y,
                    FootprintW = w,          FootprintH   = h,
                    CameraTarget = camTarget
                };
            }

            return null;
        }

        private void ClearVisual()
        {
            foreach (var h in _activeHighlights)
                if (h != null) Destroy(h.gameObject);
            _activeHighlights.Clear();
        }

        void OnDestroy() => ClearHighlight();
    }
}
