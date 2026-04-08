using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

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

        private EntityQuery                  _buildingQuery;
        private readonly HashSet<(int, int)> _highlighted = new();

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
            }
            positions.Dispose();
        }

        void OnDestroy()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (_buildingQuery != null && world != null && world.IsCreated)
                _buildingQuery.Dispose();
        }
    }
}
