using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Syncs ECS building entities (both pre-baked SubScene buildings and runtime-placed ones)
    /// to placeholder highlights on the GridRenderer.
    ///
    /// Scene setup: place on any GameObject, assign gridRenderer reference.
    /// BuildingPlacer should call Refresh() after placing a building at runtime.
    /// </summary>
    public class BuildingVisualizer : MonoBehaviour
    {
        [SerializeField] private GridRenderer gridRenderer;

        private EntityQuery                              _buildingQuery;
        private readonly HashSet<(int, int)>             _highlighted    = new();
        private readonly Dictionary<(int, int), GameObject> _spawnedCubes = new();

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;

            _buildingQuery = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingData>(),
                ComponentType.ReadOnly<GridPosition>()
            );

            // SubScene entities may not be streamed in on the first frame — poll until present
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

            var positions = _buildingQuery.ToComponentDataArray<GridPosition>(Allocator.Temp);
            for (int i = 0; i < positions.Length; i++)
            {
                int x = positions[i].Cell.x;
                int y = positions[i].Cell.y;
                var cell = (x, y);

                if (_highlighted.Contains(cell)) continue;

                gridRenderer?.SetTileHighlight(x, y, true);
                GridOccupancy.Instance?.Register(x, y);
                _highlighted.Add(cell);

                // Spawn a cube placeholder for the building
                if (!_spawnedCubes.ContainsKey(cell))
                {
                    var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.name = $"Building_{x}_{y}";
                    cube.transform.SetParent(gridRenderer.transform);
                    // Tiles use localPosition (x*cellSize, 0, y*cellSize) — cubes match with Y=0.5 to sit on top
                    cube.transform.localPosition = new Vector3(
                        x * gridRenderer.CellSize, 0.5f, y * gridRenderer.CellSize);
                    float s = gridRenderer.CellSize * 0.8f;
                    cube.transform.localScale = new Vector3(s, 1f, s);

                    var mr = cube.GetComponent<MeshRenderer>();
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    mr.receiveShadows    = false;

                    _spawnedCubes[cell] = cube;
                }
            }
            positions.Dispose();
        }

        void OnDestroy()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (_buildingQuery != null && world != null && world.IsCreated)
                _buildingQuery.Dispose();

            foreach (var go in _spawnedCubes.Values)
                if (go != null) Destroy(go);
            _spawnedCubes.Clear();
        }
    }
}
