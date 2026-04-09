using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Creates ECS conveyor segment entities at runtime from a list of grid cells.
    /// Called by ConveyorPlacementController after the player releases the drag.
    /// Each cell becomes a ConveyorSegmentData entity, linked as a doubly-linked chain.
    /// </summary>
    public class ConveyorPlacer : MonoBehaviour
    {
        [SerializeField] private ConveyorVisualizer conveyorVisualizer;

        private EntityManager _em;

        void Start()
        {
            _em = World.DefaultGameObjectInjectionWorld.EntityManager;
        }

        /// <summary>
        /// Places one conveyor segment entity per cell in the path.
        /// Assumes the path contains at least one cell and all cells are valid (non-occupied).
        /// </summary>
        public void PlaceConveyorChain(List<Vector2Int> path)
        {
            if (path == null || path.Count == 0) return;

            int count    = path.Count;
            var entities = new Entity[count];

            // Pass 1 — create entities
            for (int i = 0; i < count; i++)
            {
                var cell = path[i];

                // Determine entry / exit directions
                OutputDirection entryDir = TravelDir(i > 0 ? path[i - 1] : path[i],
                                                     i > 0 ? path[i]     : (count > 1 ? path[1] : path[0]));
                OutputDirection exitDir  = TravelDir(path[i],
                                                     i < count - 1 ? path[i + 1] : path[i]);

                // For the tail (last cell) keep the exit direction same as entry so it continues straight
                if (i == count - 1 && count > 1)
                    exitDir = TravelDir(path[i - 1], path[i]);

                var e = _em.CreateEntity(
                    typeof(ConveyorSegmentData),
                    typeof(GridPosition)
                );

                _em.SetComponentData(e, new GridPosition
                {
                    Cell = new int2(cell.x, cell.y)
                });
                _em.SetComponentData(e, new ConveyorSegmentData
                {
                    Cell          = new int2(cell.x, cell.y),
                    EntryDir      = (int)entryDir,
                    ExitDir       = (int)exitDir,
                    NextSegment   = Entity.Null,
                    PrevSegment   = Entity.Null,
                    TransportTime = 1f,
                    IsChainHead   = (i == 0)
                });

                GridOccupancy.Instance?.Register(cell.x, cell.y);
                entities[i] = e;
            }

            // Pass 2 — link chain
            for (int i = 0; i < count; i++)
            {
                var seg = _em.GetComponentData<ConveyorSegmentData>(entities[i]);
                if (i < count - 1) seg.NextSegment = entities[i + 1];
                if (i > 0)         seg.PrevSegment = entities[i - 1];
                _em.SetComponentData(entities[i], seg);
            }

            Debug.Log($"[ConveyorPlacer] Placed {count}-segment belt from {path[0]} to {path[count - 1]}.");
            conveyorVisualizer?.Refresh();
        }

        // ----------------------------------------------------------------
        // Direction helpers
        // ----------------------------------------------------------------

        /// <summary>Returns the OutputDirection of the step from 'from' to 'to'.</summary>
        public static OutputDirection TravelDir(Vector2Int from, Vector2Int to)
        {
            int dx = to.x - from.x;
            int dy = to.y - from.y;

            if (Mathf.Abs(dx) >= Mathf.Abs(dy))
                return dx >= 0 ? OutputDirection.East : OutputDirection.West;
            else
                return dy >= 0 ? OutputDirection.North : OutputDirection.South;
        }
    }
}
