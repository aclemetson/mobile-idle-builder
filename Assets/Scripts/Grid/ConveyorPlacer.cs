using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Creates ECS conveyor segment entities at runtime from a list of grid cells.
    /// Called by ConveyorPlacementController after the player releases the drag.
    ///
    /// Connection rules:
    ///   - If path[0] is an existing conveyor TAIL (NextSegment == Null), the new belt
    ///     appends after it. The existing segment's ExitDir is updated for any turn.
    ///   - If path[last] is an existing conveyor HEAD (IsChainHead == true), the new belt
    ///     feeds into it. The existing segment's EntryDir is updated for any turn.
    ///   - Intermediate cells must all be free (enforced by ConveyorPlacementController).
    /// </summary>
    public class ConveyorPlacer : MonoBehaviour
    {
        [SerializeField] private ConveyorVisualizer conveyorVisualizer;

        private EntityManager _em;
        private EntityQuery   _segmentQuery;
        private bool          _queryReady;

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;
            _em           = world.EntityManager;
            _segmentQuery = _em.CreateEntityQuery(typeof(ConveyorSegmentData));
            _queryReady   = true;
        }

        void OnDestroy()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (_queryReady && world != null && world.IsCreated)
                _segmentQuery.Dispose();
        }

        // ----------------------------------------------------------------
        // Public API
        // ----------------------------------------------------------------

        /// <summary>
        /// Places conveyor segment entities for each cell in the path.
        /// Handles connections to existing chain tails (at start) or chain heads (at end).
        /// </summary>
        public void PlaceConveyorChain(List<Vector2Int> path, OutputDirection? singleDir = null)
        {
            if (path == null || path.Count == 0) return;

            int count = path.Count;

            Dictionary<Vector2Int, Entity> segMap = BuildSegmentMap();

            bool startIsExisting = segMap.ContainsKey(path[0]);
            bool endIsExisting   = count > 1 && segMap.ContainsKey(path[count - 1]);


            int firstNew = startIsExisting ? 1 : 0;
            int lastNew  = endIsExisting   ? count - 2 : count - 1;

            // Single existing cell — nothing to do
            if (count == 1 && startIsExisting) return;

            var entities = new Entity[count];
            if (startIsExisting) entities[0]          = segMap[path[0]];
            if (endIsExisting)   entities[count - 1]  = segMap[path[count - 1]];

            // Two adjacent existing segments — link them directly
            if (firstNew > lastNew)
            {
                if (startIsExisting && endIsExisting && count == 2)
                    LinkExistingSegments(entities[0], entities[1], path[0], path[1]);
                conveyorVisualizer?.Refresh();
                return;
            }

            // ----------------------------------------------------------------
            // Pass 1 — create new segment entities
            // ----------------------------------------------------------------
            for (int i = firstNew; i <= lastNew; i++)
            {
                var cell = path[i];

                OutputDirection entryDir = i > 0
                    ? TravelDir(path[i - 1], path[i])
                    : TravelDir(path[0], count > 1 ? path[1] : path[0]);

                OutputDirection exitDir;
                if (i == lastNew && !endIsExisting)
                    // Chain tail: continue straight (same as entry)
                    exitDir = i > 0 ? TravelDir(path[i - 1], path[i]) : entryDir;
                else
                    // Mid-chain or connecting to existing head: aim toward next cell
                    exitDir = TravelDir(path[i], path[i + 1]);

                // Lone new segment with an explicitly chosen facing (single-conveyor placement):
                // there are no neighbours to derive direction from, so honour the caller's choice.
                if (count == 1 && !startIsExisting && !endIsExisting && singleDir.HasValue)
                {
                    entryDir = singleDir.Value;
                    exitDir  = singleDir.Value;
                }

                bool isHead = (i == firstNew) && !startIsExisting;

                var e = _em.CreateEntity(typeof(ConveyorSegmentData), typeof(GridPosition));
                _em.SetComponentData(e, new GridPosition { Cell = new int2(cell.x, cell.y) });
                _em.SetComponentData(e, new ConveyorSegmentData
                {
                    Cell          = new int2(cell.x, cell.y),
                    EntryDir      = (int)entryDir,
                    ExitDir       = (int)exitDir,
                    NextSegment   = Entity.Null,
                    PrevSegment   = Entity.Null,
                    TransportTime = 1f,
                    IsChainHead   = isHead
                });

                GridOccupancy.Instance?.RegisterConveyor(cell.x, cell.y);
                entities[i] = e;
            }

            // ----------------------------------------------------------------
            // Pass 2 — link new segments to each other
            // ----------------------------------------------------------------
            for (int i = firstNew; i <= lastNew; i++)
            {
                var seg = _em.GetComponentData<ConveyorSegmentData>(entities[i]);
                if (i < lastNew)  seg.NextSegment = entities[i + 1];
                if (i > firstNew) seg.PrevSegment = entities[i - 1];
                _em.SetComponentData(entities[i], seg);
            }

            // ----------------------------------------------------------------
            // Pass 3 — connect to existing segments and update their directions
            // ----------------------------------------------------------------
            if (startIsExisting)
            {
                // Connect new chain after the existing segment.
                // If the existing segment already had a successor, sever that link first
                // (the old downstream chain becomes its own independent chain head).
                var existingEnt = entities[0];
                var firstNewEnt = entities[firstNew];

                var existingSeg = _em.GetComponentData<ConveyorSegmentData>(existingEnt);
                var firstNewSeg = _em.GetComponentData<ConveyorSegmentData>(firstNewEnt);

                if (existingSeg.NextSegment != Entity.Null && _em.Exists(existingSeg.NextSegment))
                {
                    var oldNext = _em.GetComponentData<ConveyorSegmentData>(existingSeg.NextSegment);
                    oldNext.PrevSegment = Entity.Null;
                    oldNext.IsChainHead = true;
                    _em.SetComponentData(existingSeg.NextSegment, oldNext);
                }

                var exitToNew = TravelDir(path[0], path[firstNew]);

                existingSeg.ExitDir     = (int)exitToNew;
                existingSeg.NextSegment = firstNewEnt;
                _em.SetComponentData(existingEnt, existingSeg);

                firstNewSeg.EntryDir    = (int)exitToNew;
                firstNewSeg.PrevSegment = existingEnt;
                firstNewSeg.IsChainHead = false;
                _em.SetComponentData(firstNewEnt, firstNewSeg);

                conveyorVisualizer?.RefreshBelt(path[0].x, path[0].y,
                    existingSeg.EntryDir, existingSeg.ExitDir);
            }

            if (endIsExisting)
            {
                // Connect new chain into the existing segment.
                // If the existing segment had a predecessor, sever it first —
                // that upstream tail becomes a dead end (NextSegment cleared).
                var lastNewEnt  = entities[lastNew];
                var existingEnt = entities[count - 1];

                var lastNewSeg  = _em.GetComponentData<ConveyorSegmentData>(lastNewEnt);
                var existingSeg = _em.GetComponentData<ConveyorSegmentData>(existingEnt);

                if (existingSeg.PrevSegment != Entity.Null && _em.Exists(existingSeg.PrevSegment))
                {
                    var oldPrev = _em.GetComponentData<ConveyorSegmentData>(existingSeg.PrevSegment);
                    oldPrev.NextSegment = Entity.Null;
                    _em.SetComponentData(existingSeg.PrevSegment, oldPrev);
                }

                var exitToExisting = TravelDir(path[lastNew], path[count - 1]);

                lastNewSeg.ExitDir     = (int)exitToExisting;
                lastNewSeg.NextSegment = existingEnt;
                _em.SetComponentData(lastNewEnt, lastNewSeg);

                existingSeg.EntryDir    = (int)exitToExisting;
                existingSeg.PrevSegment = lastNewEnt;
                existingSeg.IsChainHead = false;
                _em.SetComponentData(existingEnt, existingSeg);

                conveyorVisualizer?.RefreshBelt(path[count - 1].x, path[count - 1].y,
                    existingSeg.EntryDir, existingSeg.ExitDir);
            }

            int newCount = lastNew - firstNew + 1;
            GameLogger.Develop($"[ConveyorPlacer] Placed {newCount} segment(s) from {path[firstNew]} to {path[lastNew]}. " +
                      $"startConnected={startIsExisting} endConnected={endIsExisting}");

            conveyorVisualizer?.Refresh();
        }

        /// <summary>
        /// Removes the conveyor segment at (x, y), if one exists, and re-links its chain
        /// neighbours so the chain stays consistent:
        ///   - the predecessor becomes a tail (NextSegment = Null),
        ///   - the successor becomes a chain head (PrevSegment = Null, IsChainHead = true).
        /// Also clears grid occupancy and the belt visual. Returns true if a segment was removed.
        ///
        /// Single source of truth for conveyor removal — shared by DeconstructController and the
        /// conveyor placement controller's Destroy mode. Callers are responsible for persisting.
        /// </summary>
        public bool RemoveSegmentAt(int x, int y)
        {
            if (!_queryReady) return false;

            var entities = _segmentQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var e   = entities[i];
                var seg = _em.GetComponentData<ConveyorSegmentData>(e);
                if (seg.Cell.x != x || seg.Cell.y != y) continue;

                // Unlink from predecessor — it becomes the new tail
                if (seg.PrevSegment != Entity.Null && _em.Exists(seg.PrevSegment))
                {
                    var prev = _em.GetComponentData<ConveyorSegmentData>(seg.PrevSegment);
                    prev.NextSegment = Entity.Null;
                    _em.SetComponentData(seg.PrevSegment, prev);
                }

                // Unlink from successor — it becomes the new chain head
                if (seg.NextSegment != Entity.Null && _em.Exists(seg.NextSegment))
                {
                    var next = _em.GetComponentData<ConveyorSegmentData>(seg.NextSegment);
                    next.PrevSegment = Entity.Null;
                    next.IsChainHead = true;
                    _em.SetComponentData(seg.NextSegment, next);
                }

                _em.DestroyEntity(e);
                GridOccupancy.Instance?.UnregisterConveyor(x, y);
                conveyorVisualizer?.RemoveBelt(x, y);

                entities.Dispose();
                GameLogger.Develop($"[ConveyorPlacer] Removed conveyor segment at ({x},{y}).");
                return true;
            }

            entities.Dispose();
            return false;
        }

        // ----------------------------------------------------------------
        // Private helpers
        // ----------------------------------------------------------------

        /// <summary>
        /// Directly links two adjacent existing segments (tail→head).
        /// Updates ExitDir/EntryDir for any turn and refreshes their visuals.
        /// </summary>
        private void LinkExistingSegments(Entity tail, Entity head, Vector2Int tailCell, Vector2Int headCell)
        {
            var tailSeg = _em.GetComponentData<ConveyorSegmentData>(tail);
            var headSeg = _em.GetComponentData<ConveyorSegmentData>(head);

            // Sever the tail's old successor (if any) before linking the new head
            if (tailSeg.NextSegment != Entity.Null && _em.Exists(tailSeg.NextSegment))
            {
                var oldNext = _em.GetComponentData<ConveyorSegmentData>(tailSeg.NextSegment);
                oldNext.PrevSegment = Entity.Null;
                oldNext.IsChainHead = true;
                _em.SetComponentData(tailSeg.NextSegment, oldNext);
            }

            // Sever the head's old predecessor (if any) before linking the new tail
            if (headSeg.PrevSegment != Entity.Null && _em.Exists(headSeg.PrevSegment))
            {
                var oldPrev = _em.GetComponentData<ConveyorSegmentData>(headSeg.PrevSegment);
                oldPrev.NextSegment = Entity.Null;
                _em.SetComponentData(headSeg.PrevSegment, oldPrev);
            }

            var dir = TravelDir(tailCell, headCell);

            tailSeg.ExitDir     = (int)dir;
            tailSeg.NextSegment = head;
            _em.SetComponentData(tail, tailSeg);

            headSeg.EntryDir    = (int)dir;
            headSeg.PrevSegment = tail;
            headSeg.IsChainHead = false;
            _em.SetComponentData(head, headSeg);

            conveyorVisualizer?.RefreshBelt(tailCell.x, tailCell.y, tailSeg.EntryDir, tailSeg.ExitDir);
            conveyorVisualizer?.RefreshBelt(headCell.x, headCell.y, headSeg.EntryDir, headSeg.ExitDir);

            GameLogger.Develop($"[ConveyorPlacer] Directly linked existing segments {tailCell} → {headCell}.");
        }

        /// <summary>Builds a cell→entity lookup from all existing conveyor segments.</summary>
        private Dictionary<Vector2Int, Entity> BuildSegmentMap()
        {
            var entities = _segmentQuery.ToEntityArray(Allocator.Temp);
            var map      = new Dictionary<Vector2Int, Entity>(entities.Length);

            for (int i = 0; i < entities.Length; i++)
            {
                var seg  = _em.GetComponentData<ConveyorSegmentData>(entities[i]);
                map[new Vector2Int(seg.Cell.x, seg.Cell.y)] = entities[i];
            }

            entities.Dispose();
            return map;
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
