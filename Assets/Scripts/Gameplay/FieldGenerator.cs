using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Spawns resource fields randomly on the grid at game start.
    /// Configure the field list in the Inspector to add/remove field types
    /// and counts as the player progresses.
    ///
    /// Default setup: 2 adjacent quark fields + 1 electron field.
    /// Fields are placed at least <see cref="edgeMargin"/> cells from the grid edge.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public class FieldGenerator : MonoBehaviour
    {
        // Keyed by grid cell — lets BuildingPlacementController check field compatibility at any cell.
        private static readonly Dictionary<Vector2Int, FieldSO>      _fieldMap    = new();
        private static readonly Dictionary<Vector2Int, FieldInstance> _instanceMap = new();

        /// <summary>Returns the FieldSO at a grid cell, or null if none.</summary>
        public static FieldSO GetFieldAt(int x, int y) =>
            _fieldMap.TryGetValue(new Vector2Int(x, y), out var f) ? f : null;

        /// <summary>Returns the FieldInstance at a grid cell, or null if none.</summary>
        public static FieldInstance GetFieldInstanceAt(int x, int y) =>
            _instanceMap.TryGetValue(new Vector2Int(x, y), out var fi) ? fi : null;

        [Serializable]
        public struct FieldEntry
        {
            public FieldSO fieldDefinition;
            [Min(1)] public int count;
            [Tooltip("Place the first two fields as adjacent neighbors (e.g. quark pair).")]
            public bool groupAdjacent;
        }

        [SerializeField] private GridRenderer gridRenderer;
        [SerializeField] [Min(0)] private int edgeMargin = 2;
        [Tooltip("No field may spawn within this many cells (Chebyshev) of a Maxwell's Demon building.")]
        [SerializeField] [Min(0)] private int demonClearance = 2;
        [SerializeField] private List<FieldEntry> fields = new();

        /// <summary>Cells excluded from field placement because they are too close to an EntropySink building.</summary>
        private readonly HashSet<Vector2Int> _demonExclusionZone = new();

        private static readonly Vector2Int[] Cardinals =
        {
            new( 1,  0),
            new(-1,  0),
            new( 0,  1),
            new( 0, -1),
        };

        void OnDestroy() { _fieldMap.Clear(); _instanceMap.Clear(); } // clean up between Play sessions in the editor

        IEnumerator Start()
        {
            _fieldMap.Clear();
            if (gridRenderer == null)
            {
                Debug.LogError("[FieldGenerator] GridRenderer reference is missing.", this);
                yield break;
            }

            // Wait for baked buildings from the SubScene to appear in the ECS world,
            // then register their footprints in GridOccupancy before placing any fields.
            yield return RegisterBakedBuildings();

            // Mark cells too close to Maxwell's Demon as off-limits for field spawning.
            BuildDemonExclusionZone();

            var candidates = BuildCandidateList();
            Shuffle(candidates);

            int candidateIndex = 0;

            foreach (var entry in fields)
            {
                if (entry.fieldDefinition == null) continue;

                if (entry.groupAdjacent && entry.count >= 2)
                {
                    PlaceAdjacentGroup(entry, ref candidateIndex, candidates);
                }
                else
                {
                    for (int i = 0; i < entry.count; i++)
                        PlaceSingle(entry.fieldDefinition, ref candidateIndex, candidates);
                }
            }
        }

        /// <summary>
        /// Waits up to 5 seconds for baked building entities to appear in the ECS world,
        /// then registers every occupied cell into GridOccupancy so field placement can
        /// avoid them. Safe to call when no buildings exist — exits immediately.
        /// </summary>
        private IEnumerator RegisterBakedBuildings()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) yield break;

            var buildingQuery = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingData>(),
                ComponentType.ReadOnly<GridPosition>());

            float timeout = 5f;
            while (buildingQuery.IsEmpty && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            if (buildingQuery.IsEmpty || GridOccupancy.Instance == null)
            {
                buildingQuery.Dispose();
                yield break;
            }

            // Collect multi-cell footprints
            var footprintMap = new Dictionary<(int, int), (int, int)>();
            var footprintQuery = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<GridPosition>(),
                ComponentType.ReadOnly<BuildingFootprint>());

            if (!footprintQuery.IsEmpty)
            {
                var fpPos  = footprintQuery.ToComponentDataArray<GridPosition>(Allocator.Temp);
                var fpData = footprintQuery.ToComponentDataArray<BuildingFootprint>(Allocator.Temp);
                for (int i = 0; i < fpPos.Length; i++)
                    footprintMap[(fpPos[i].Cell.x, fpPos[i].Cell.y)] = (fpData[i].Width, fpData[i].Height);
                fpPos.Dispose();
                fpData.Dispose();
            }
            footprintQuery.Dispose();

            // Register every building cell — RegisterRect is idempotent (HashSet internally)
            var positions = buildingQuery.ToComponentDataArray<GridPosition>(Allocator.Temp);
            for (int i = 0; i < positions.Length; i++)
            {
                int x = positions[i].Cell.x;
                int y = positions[i].Cell.y;
                int fw = 1, fh = 1;
                if (footprintMap.TryGetValue((x, y), out var fp)) { fw = fp.Item1; fh = fp.Item2; }
                GridOccupancy.Instance.RegisterRect(x, y, fw, fh);
            }
            positions.Dispose();
            buildingQuery.Dispose();
        }

        // ----------------------------------------------------------------
        // Placement helpers
        // ----------------------------------------------------------------

        private void PlaceAdjacentGroup(FieldEntry entry, ref int index, List<Vector2Int> candidates)
        {
            // Find first valid anchor cell
            Vector2Int anchor = FindNextFreeCandidate(ref index, candidates);
            if (anchor.x < 0) return;

            OccupyAndSpawn(anchor.x, anchor.y, entry.fieldDefinition);

            // Place second field adjacent to anchor
            bool placedSecond = false;
            foreach (var dir in Cardinals)
            {
                var neighbor = anchor + dir;
                if (!IsValidCandidate(neighbor.x, neighbor.y)) continue;
                if (GridOccupancy.Instance != null && GridOccupancy.Instance.IsOccupied(neighbor.x, neighbor.y)) continue;

                OccupyAndSpawn(neighbor.x, neighbor.y, entry.fieldDefinition);
                placedSecond = true;
                break;
            }

            if (!placedSecond)
                Debug.LogWarning($"[FieldGenerator] Could not place adjacent neighbor for {entry.fieldDefinition.displayName}. Placing fallback at next candidate.");

            // Place any remaining count beyond the first pair
            for (int i = 2; i < entry.count; i++)
                PlaceSingle(entry.fieldDefinition, ref index, candidates);
        }

        private void PlaceSingle(FieldSO fieldSO, ref int index, List<Vector2Int> candidates)
        {
            Vector2Int cell = FindNextFreeCandidate(ref index, candidates);
            if (cell.x < 0) return;
            OccupyAndSpawn(cell.x, cell.y, fieldSO);
        }

        private Vector2Int FindNextFreeCandidate(ref int index, List<Vector2Int> candidates)
        {
            while (index < candidates.Count)
            {
                var c = candidates[index++];
                // Skip cells already claimed by another field or a pre-placed building
                if (_fieldMap.ContainsKey(c)) continue;
                if (GridOccupancy.Instance != null && GridOccupancy.Instance.IsOccupied(c.x, c.y)) continue;
                return c;
            }
            Debug.LogWarning("[FieldGenerator] Ran out of candidate cells for field placement.");
            return new Vector2Int(-1, -1);
        }

        private void OccupyAndSpawn(int x, int y, FieldSO fieldSO)
        {
            // Do NOT register in GridOccupancy — _fieldMap tracks field cells.
            // GridOccupancy is reserved for placed buildings so RestoreCell colours correctly.
            var key = new Vector2Int(x, y);
            _fieldMap[key] = fieldSO;

            float cs  = gridRenderer.CellSize;
            var   pos = new Vector3(x * cs, 0.5f, y * cs);

            var go = new GameObject();
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.position = pos;

            var instance = go.AddComponent<FieldInstance>();
            instance.Initialize(fieldSO);
            _instanceMap[key] = instance;

            // Collider lets ManualFieldCollector confirm the player tapped this specific field.
            var col = go.AddComponent<SphereCollider>();
            col.center = new Vector3(0f, 0.5f, 0f);
            col.radius = 0.5f;

            var effect = go.AddComponent<FieldEffect>();
            effect.Initialize(fieldSO.fieldColor);
        }

        // ----------------------------------------------------------------
        // Exclusion zone
        // ----------------------------------------------------------------

        /// <summary>
        /// Populates <see cref="_demonExclusionZone"/> with every grid cell whose Chebyshev
        /// distance to any <see cref="EntropySinkTag"/> building is ≤ <see cref="demonClearance"/>.
        /// </summary>
        private void BuildDemonExclusionZone()
        {
            _demonExclusionZone.Clear();
            if (demonClearance <= 0) return;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;

            using var query = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<EntropySinkTag>(),
                ComponentType.ReadOnly<GridPosition>()
            );

            if (query.IsEmpty) return;

            var positions = query.ToComponentDataArray<GridPosition>(Allocator.Temp);
            for (int i = 0; i < positions.Length; i++)
            {
                int cx = positions[i].Cell.x;
                int cy = positions[i].Cell.y;
                for (int dx = -demonClearance; dx <= demonClearance; dx++)
                for (int dy = -demonClearance; dy <= demonClearance; dy++)
                    _demonExclusionZone.Add(new Vector2Int(cx + dx, cy + dy));
            }
            positions.Dispose();
        }

        // ----------------------------------------------------------------
        // Candidate list
        // ----------------------------------------------------------------

        private List<Vector2Int> BuildCandidateList()
        {
            var list = new List<Vector2Int>();
            int w = gridRenderer.Width;
            int h = gridRenderer.Height;

            for (int x = edgeMargin; x < w - edgeMargin; x++)
            for (int y = edgeMargin; y < h - edgeMargin; y++)
            {
                if (!_demonExclusionZone.Contains(new Vector2Int(x, y)))
                    list.Add(new Vector2Int(x, y));
            }

            return list;
        }

        private bool IsValidCandidate(int x, int y)
        {
            int w = gridRenderer.Width;
            int h = gridRenderer.Height;
            return x >= edgeMargin && x < w - edgeMargin &&
                   y >= edgeMargin && y < h - edgeMargin &&
                   !_demonExclusionZone.Contains(new Vector2Int(x, y));
        }

        private static void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
