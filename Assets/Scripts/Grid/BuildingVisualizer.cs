using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Syncs ECS building entities to placeholder highlights and port arrows on the GridRenderer.
    /// BuildingPlacer should call Refresh() after placing a building at runtime.
    /// </summary>
    public class BuildingVisualizer : MonoBehaviour
    {
        [SerializeField] private GridRenderer gridRenderer;
        [SerializeField] private Material     _buildingMaterial;  // URP Unlit Transparent — assign in Inspector

        private EntityQuery                                       _buildingQuery;
        private EntityQuery                                       _footprintQuery;
        private EntityQuery                                       _portQuery;
        private EntityQuery                                       _powerStatusQuery;
        private bool                                              _buildingQueryReady;
        private bool                                              _footprintQueryReady;
        private bool                                              _portQueryReady;
        private bool                                              _powerStatusQueryReady;
        private float                                             _powerTintTimer;
        private readonly Dictionary<(int, int), (int, int)>      _footprints        = new();
        private readonly HashSet<(int, int)>                      _highlighted       = new();
        private readonly Dictionary<(int, int), GameObject>       _spawnedCubes      = new();
        private readonly HashSet<(int, int)>                      _portedCells       = new();
        private readonly Dictionary<(int, int), List<GameObject>> _spawnedPortArrows = new();

        // Maps every occupied cell (including non-anchor cells of multi-tile buildings)
        // back to the anchor cell so SetHover can find the right cube.
        private readonly Dictionary<(int, int), (int, int)>      _cellToAnchor      = new();

        private (int, int) _hoveredCell = (-1, -1);

        private static readonly Color DefaultCubeColor   = Color.white;
        private static readonly Color HoverCubeColor     = new Color(0.35f, 0.9f, 1f);
        private static readonly Color UnpoweredCubeColor = new Color(0.9f,  0.25f, 0.25f); // red — no power in range

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;

            _buildingQuery = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingData>(),
                ComponentType.ReadOnly<GridPosition>()
            );
            _buildingQueryReady = true;

            _footprintQuery = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<GridPosition>(),
                ComponentType.ReadOnly<BuildingFootprint>()
            );
            _footprintQueryReady = true;

            _portQuery = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<GridPosition>(),
                ComponentType.ReadOnly<PlacedPortData>()
            );
            _portQueryReady = true;

            _powerStatusQuery = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<GridPosition>(),
                ComponentType.ReadOnly<PowerStatus>()
            );
            _powerStatusQueryReady = true;

            StartCoroutine(ScanOnceLoaded());
        }

        private IEnumerator ScanOnceLoaded()
        {
            float timeout = 5f;
            while (_buildingQuery.IsEmpty && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            if (!_buildingQuery.IsEmpty)
                Refresh();
        }

        /// <summary>
        /// Highlights any building cells not yet marked. Safe to call repeatedly —
        /// already-highlighted cells are skipped.
        /// </summary>
        public void Refresh()
        {
            if (_buildingQuery == null || _buildingQuery.IsEmpty) return;

            // Pass 1: collect footprints
            if (_footprintQueryReady && !_footprintQuery.IsEmpty)
            {
                var fpPositions = _footprintQuery.ToComponentDataArray<GridPosition>(Allocator.Temp);
                var fpData      = _footprintQuery.ToComponentDataArray<BuildingFootprint>(Allocator.Temp);
                for (int i = 0; i < fpPositions.Length; i++)
                    _footprints[(fpPositions[i].Cell.x, fpPositions[i].Cell.y)] = (fpData[i].Width, fpData[i].Height);
                fpPositions.Dispose();
                fpData.Dispose();
            }

            // Pass 2: highlight tiles and spawn cube placeholders
            var positions = _buildingQuery.ToComponentDataArray<GridPosition>(Allocator.Temp);
            for (int i = 0; i < positions.Length; i++)
            {
                int x    = positions[i].Cell.x;
                int y    = positions[i].Cell.y;
                var cell = (x, y);

                if (_highlighted.Contains(cell)) continue;

                int fw = 1, fh = 1;
                if (_footprints.TryGetValue(cell, out var fp)) { fw = fp.Item1; fh = fp.Item2; }

                for (int dx = 0; dx < fw; dx++)
                    for (int dy = 0; dy < fh; dy++)
                    {
                        gridRenderer?.SetTileHighlight(x + dx, y + dy, true);
                        GridOccupancy.Instance?.Register(x + dx, y + dy);
                        _cellToAnchor[(x + dx, y + dy)] = cell;
                    }
                _highlighted.Add(cell);

                if (!_spawnedCubes.ContainsKey(cell))
                {
                    var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.name = $"Building_{x}_{y}";
                    cube.transform.SetParent(gridRenderer.transform);
                    float cs = gridRenderer.CellSize;
                    cube.transform.localPosition = new Vector3(
                        (x + (fw - 1) * 0.5f) * cs, 0.5f, (y + (fh - 1) * 0.5f) * cs);
                    float gap = cs * 0.2f;
                    cube.transform.localScale = new Vector3(cs * fw - gap, 1f, cs * fh - gap);
                    var mr = cube.GetComponent<MeshRenderer>();
                    if (_buildingMaterial != null)
                        mr.sharedMaterial = _buildingMaterial;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    mr.receiveShadows    = false;

                    // PresenceReceiver owns all colour state for this cube
                    var pr = cube.AddComponent<PresenceReceiver>();
                    pr.SetBaseColor(DefaultCubeColor);

                    _spawnedCubes[cell] = cube;
                }
            }
            positions.Dispose();

            // Pass 3: draw port arrows from SO port layout
            if (!_portQueryReady || _portQuery.IsEmpty) return;

            var entities = _portQuery.ToEntityArray(Allocator.Temp);
            var em       = World.DefaultGameObjectInjectionWorld.EntityManager;
            float portHeight = 1.1f;
            float cs2        = gridRenderer.CellSize;

            for (int i = 0; i < entities.Length; i++)
            {
                var pos  = em.GetComponentData<GridPosition>(entities[i]);
                var cell = (pos.Cell.x, pos.Cell.y);

                if (_portedCells.Contains(cell)) continue;

                var ports     = em.GetBuffer<PlacedPortData>(entities[i]);
                var arrowList = new List<GameObject>();

                for (int p = 0; p < ports.Length; p++)
                {
                    var  port   = ports[p];
                    int  wx     = port.CellX;
                    int  wy     = port.CellY;
                    var  dir    = (OutputDirection)port.Facing;
                    bool isOut  = port.PortType == (int)PortType.Output;

                    // localFacing = direction items arrive from (= conveyor exit direction).
                    // Output: arrow on facing edge, pointing outward.
                    // Input:  arrow on opposite edge (exterior face), pointing inward (= facing direction).
                    var     oppDir     = (OutputDirection)(((int)dir + 2) % 4);
                    Vector3 edgeOffset = isOut ? FacingEdgeOffset(dir, cs2) : FacingEdgeOffset(oppDir, cs2);
                    var     displayDir = dir;

                    var arrowGO = new GameObject($"Port_{(isOut ? "Out" : "In")}_{wx}_{wy}");
                    arrowGO.transform.SetParent(gridRenderer.transform, worldPositionStays: false);
                    arrowGO.transform.localPosition = new Vector3(wx * cs2, portHeight, wy * cs2) + edgeOffset;

                    var arrow = arrowGO.AddComponent<OutputArrow>();
                    arrow.Initialize(displayDir, isOut ? OutputArrow.PlacedColor : OutputArrow.InputColor);
                    arrowList.Add(arrowGO);
                }

                _portedCells.Add(cell);
                _spawnedPortArrows[cell] = arrowList;
            }

            entities.Dispose();
        }

        // ----------------------------------------------------------------
        // Power tint — persistently reddens consumer cubes with no generator in range
        // ----------------------------------------------------------------

        void Update()
        {
            _powerTintTimer += Time.deltaTime;
            if (_powerTintTimer < 0.4f) return;
            _powerTintTimer = 0f;
            RefreshPowerTint();
        }

        private void RefreshPowerTint()
        {
            if (!_powerStatusQueryReady || _powerStatusQuery.IsEmpty) return;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            var entities = _powerStatusQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var pos    = em.GetComponentData<GridPosition>(entities[i]);
                var status = em.GetComponentData<PowerStatus>(entities[i]);
                var cell   = (pos.Cell.x, pos.Cell.y);

                // Don't fight the hover highlight; it reasserts on the next tick after un-hover.
                if (cell == _hoveredCell) continue;
                if (_spawnedCubes.TryGetValue(cell, out var cube) && cube != null)
                    ApplyCubeColor(cube, status.IsConnected == 0 ? UnpoweredCubeColor : DefaultCubeColor);
            }
            entities.Dispose();
        }

        private static Vector3 FacingEdgeOffset(OutputDirection dir, float cellSize)
        {
            float h = cellSize * 0.5f;
            return dir switch
            {
                OutputDirection.North => new Vector3(0,   0,  h),
                OutputDirection.East  => new Vector3( h,  0,  0),
                OutputDirection.South => new Vector3(0,   0, -h),
                OutputDirection.West  => new Vector3(-h,  0,  0),
                _                     => Vector3.zero
            };
        }

        /// <summary>
        /// Removes all visual artifacts for the building anchored at (x, y).
        /// Call after destroying the corresponding ECS entity.
        /// </summary>
        public void RemoveBuilding(int x, int y)
        {
            var cell = (x, y);

            int fw = 1, fh = 1;
            if (_footprints.TryGetValue(cell, out var fp)) { fw = fp.Item1; fh = fp.Item2; }

            for (int dx = 0; dx < fw; dx++)
                for (int dy = 0; dy < fh; dy++)
                {
                    gridRenderer?.SetTileHighlight(x + dx, y + dy, false);
                    _cellToAnchor.Remove((x + dx, y + dy));
                }

            if (_hoveredCell == cell) _hoveredCell = (-1, -1);

            _highlighted.Remove(cell);
            _footprints.Remove(cell);

            if (_spawnedCubes.TryGetValue(cell, out var cube))
            {
                if (cube != null) Destroy(cube);
                _spawnedCubes.Remove(cell);
            }

            if (_spawnedPortArrows.TryGetValue(cell, out var arrows))
            {
                foreach (var go in arrows)
                    if (go != null) Destroy(go);
                _spawnedPortArrows.Remove(cell);
            }
            _portedCells.Remove(cell);
        }

        // ----------------------------------------------------------------
        // Hover highlight
        // ----------------------------------------------------------------

        /// <summary>
        /// Tints the building at the given grid cell (any cell within its footprint).
        /// Automatically restores the previously hovered building to its default color.
        /// </summary>
        public void SetHover(int x, int y)
        {
            if (!_cellToAnchor.TryGetValue((x, y), out var anchorCell)) return;
            if (anchorCell == _hoveredCell) return;

            // Restore previous
            if (_spawnedCubes.TryGetValue(_hoveredCell, out var prev) && prev != null)
                ApplyCubeColor(prev, DefaultCubeColor);

            _hoveredCell = anchorCell;

            if (_spawnedCubes.TryGetValue(anchorCell, out var cur) && cur != null)
                ApplyCubeColor(cur, HoverCubeColor);
        }

        /// <summary>Removes the hover highlight from whichever building is currently hovered.</summary>
        public void ClearHover()
        {
            if (_spawnedCubes.TryGetValue(_hoveredCell, out var prev) && prev != null)
                ApplyCubeColor(prev, DefaultCubeColor);
            _hoveredCell = (-1, -1);
        }

        private static void ApplyCubeColor(GameObject go, Color color)
        {
            // Route through PresenceReceiver so the presence boost is preserved on top
            var pr = go.GetComponent<PresenceReceiver>();
            if (pr != null) { pr.SetBaseColor(color); return; }

            // Fallback for any cube that doesn't have a PresenceReceiver (shouldn't happen)
            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null) return;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", color);
            mr.SetPropertyBlock(mpb);
        }

        void OnDestroy()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated)
            {
                if (_buildingQueryReady)    _buildingQuery.Dispose();
                if (_footprintQueryReady)   _footprintQuery.Dispose();
                if (_portQueryReady)        _portQuery.Dispose();
                if (_powerStatusQueryReady) _powerStatusQuery.Dispose();
            }

            foreach (var go in _spawnedCubes.Values)
                if (go != null) Destroy(go);
            _spawnedCubes.Clear();

            foreach (var list in _spawnedPortArrows.Values)
                foreach (var go in list)
                    if (go != null) Destroy(go);
            _spawnedPortArrows.Clear();
        }
    }
}
