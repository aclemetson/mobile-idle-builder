using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Creates and links ECS conveyor segment entities at runtime from a list of grid cells.
    /// Called by ConveyorPlacementController after the player confirms a run, and by
    /// GridSaveService when replaying saved chains on load.
    ///
    /// Connectivity model (see ConveyorData.cs): a cell's single output is its <c>ExitDir</c>.
    /// Everything else — NextSegment, PrevSegment, EntryDir, IsChainHead — is DERIVED from the
    /// ExitDir geometry of every segment by <see cref="RelinkAll"/>. This makes arbitrary graphs,
    /// including 2-3 input merges into one output, correct by construction:
    ///   - A cell may take flow from multiple inbound segments (any segment whose ExitDir points at
    ///     it), but it always has exactly one output. Placement REJECTS a second output on a cell.
    ///   - Merge inbound count is capped at 3.
    ///
    /// After (re)linking, <see cref="TryAutoTurnEnds"/> orients dead-end cells toward adjacent
    /// building ports so the river always visually flows INTO a building input / OUT of an output.
    /// </summary>
    public class ConveyorPlacer : MonoBehaviour
    {
        private const int MaxInbounds = 3;

        [SerializeField] private ConveyorVisualizer conveyorVisualizer;

        private EntityManager _em;
        private EntityQuery   _segmentQuery;
        private EntityQuery   _buildingQuery;
        private bool          _queryReady;

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;
            _em            = world.EntityManager;
            _segmentQuery  = _em.CreateEntityQuery(typeof(ConveyorSegmentData));
            _buildingQuery = _em.CreateEntityQuery(typeof(BuildingData), typeof(GridPosition));
            _queryReady    = true;
        }

        void OnDestroy()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (_queryReady && world != null && world.IsCreated)
            {
                _segmentQuery.Dispose();
                _buildingQuery.Dispose();
            }
        }

        // ----------------------------------------------------------------
        // Public API
        // ----------------------------------------------------------------

        /// <summary>
        /// Lays down conveyor segments along <paramref name="path"/> (upstream → downstream): the
        /// segment at path[i] outputs toward path[i+1]. Existing cells anywhere in the path are
        /// reused (so a run can merge into an existing belt, and saved merge routes replay cleanly).
        /// Rejects a run that would fork a cell's output or push a merge past 3 inbounds.
        /// </summary>
        public void PlaceConveyorChain(List<Vector2Int> path, OutputDirection? singleDir = null)
        {
            if (!_queryReady || path == null || path.Count == 0) return;

            int count = path.Count;

            // Snapshot existing geometry: cell→entity (to reuse cells) and cell→ExitDir (to validate).
            var cellMap  = new Dictionary<Vector2Int, Entity>();
            var cellExit = new Dictionary<int2, int>();
            var existing = _segmentQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < existing.Length; i++)
            {
                var seg = _em.GetComponentData<ConveyorSegmentData>(existing[i]);
                cellMap[new Vector2Int(seg.Cell.x, seg.Cell.y)] = existing[i];
                cellExit[seg.Cell] = seg.ExitDir;
            }
            existing.Dispose();

            var pathCells = new int2[count];
            for (int i = 0; i < count; i++) pathCells[i] = new int2(path[i].x, path[i].y);

            if (!ConveyorTopology.CanPlace(cellExit, pathCells, MaxInbounds, out string reason))
            {
                GameLogger.Warning($"[ConveyorPlacer] Placement rejected: {reason}.");
                return;
            }

            var newCells = new HashSet<int2>();

            for (int i = 0; i < count; i++)
            {
                var cell = path[i];

                if (!cellMap.TryGetValue(cell, out var e))
                {
                    // Default output: continue in the travel direction (straight). Non-terminal
                    // cells get overwritten just below; a lone belt honours the chosen singleDir.
                    OutputDirection exit;
                    if (count == 1)             exit = singleDir ?? OutputDirection.North;
                    else if (i < count - 1)     exit = TravelDir(path[i], path[i + 1]);
                    else                        exit = TravelDir(path[i - 1], path[i]);

                    e = _em.CreateEntity(typeof(ConveyorSegmentData), typeof(GridPosition));
                    _em.SetComponentData(e, new GridPosition { Cell = new int2(cell.x, cell.y) });
                    _em.SetComponentData(e, new ConveyorSegmentData
                    {
                        Cell          = new int2(cell.x, cell.y),
                        EntryDir      = (int)exit,
                        ExitDir       = (int)exit,
                        NextSegment   = Entity.Null,
                        PrevSegment   = Entity.Null,
                        TransportTime = 1f,
                        IsChainHead   = true,
                        MergeCursor   = 0
                    });
                    GridOccupancy.Instance?.RegisterConveyor(cell.x, cell.y);
                    cellMap[cell] = e;
                    newCells.Add(new int2(cell.x, cell.y));
                }
                else if (i < count - 1)
                {
                    // Existing, non-terminal: redirect its output along the path (extending from an
                    // existing tail, or a load-replay re-asserting identical geometry — a no-op). The
                    // forward link is now an INTENTIONAL part of this run, so clear any dead-end block.
                    var s = _em.GetComponentData<ConveyorSegmentData>(e);
                    s.ExitDir       = (int)TravelDir(path[i], path[i + 1]);
                    s.OutputBlocked = false;
                    _em.SetComponentData(e, s);
                }
                // Existing terminal cell keeps its own ExitDir (and downstream / output).
            }

            // A run that merely runs UP TO an existing belt (without an endpoint landing on it) must
            // not auto-merge: block the offending output so the belts stay separate dead-ends.
            BlockAdjacencyMerges(path, cellMap, newCells);

            RebuildTopology();
            conveyorVisualizer?.RefreshAll();

            GameLogger.Develop($"[ConveyorPlacer] Placed run {path[0]} -> {path[count - 1]} ({count} cell(s)).");
        }

        /// <summary>
        /// Marks each cell in <paramref name="cells"/> as a blocked dead-end (its forward output stays
        /// disconnected), then rebuilds connectivity. Called on load to restore the "ran up to but did
        /// not merge with" relationships that the saved chain geometry alone cannot express.
        /// </summary>
        public void ApplyBlockedOutputs(IReadOnlyList<Vector2Int> cells)
        {
            if (!_queryReady || cells == null || cells.Count == 0) return;

            var blockedSet = new HashSet<int2>();
            foreach (var c in cells) blockedSet.Add(new int2(c.x, c.y));

            var entities = _segmentQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var s = _em.GetComponentData<ConveyorSegmentData>(entities[i]);
                if (!blockedSet.Contains(s.Cell)) continue;
                s.OutputBlocked = true;
                _em.SetComponentData(entities[i], s);
            }
            entities.Dispose();

            RebuildTopology();
            conveyorVisualizer?.RefreshAll();
        }

        /// <summary>
        /// After a run is placed, blocks the output crossing the boundary between a NEWLY CREATED cell
        /// of this run and a PRE-EXISTING belt outside the run — in either direction (the new cell
        /// pointing into an existing belt, or an existing belt pointing into the new cell). This enforces
        /// "merge only where an endpoint coincides": adjacency alone never connects belts.
        ///
        /// Only boundaries touching a brand-new cell are blocked. Reused/pre-existing cells keep their
        /// links, so a run that ends ON an existing belt still merges, and an existing belt that already
        /// merges into a reused cell is never severed when a second branch joins it.
        /// </summary>
        private void BlockAdjacencyMerges(List<Vector2Int> path, Dictionary<Vector2Int, Entity> cellMap,
                                          HashSet<int2> newCells)
        {
            if (newCells.Count == 0) return;

            var pathSet = new HashSet<int2>();
            foreach (var c in path) pathSet.Add(new int2(c.x, c.y));

            foreach (var p in newCells)
            {
                if (!cellMap.TryGetValue(new Vector2Int(p.x, p.y), out var pe)) continue;
                var pSeg = _em.GetComponentData<ConveyorSegmentData>(pe);

                for (int d = 0; d < 4; d++)
                {
                    int2 q = AdjacentCell(p, d);
                    if (pathSet.Contains(q)) continue;          // same run — an intentional link
                    if (!cellMap.TryGetValue(new Vector2Int(q.x, q.y), out var qe)) continue; // not a conveyor

                    var qSeg = _em.GetComponentData<ConveyorSegmentData>(qe);

                    // The new cell points forward into the existing belt — block the new cell's output.
                    int2 pExitCell = AdjacentCell(p, pSeg.ExitDir);
                    if (pExitCell.x == q.x && pExitCell.y == q.y && !pSeg.OutputBlocked)
                    {
                        pSeg.OutputBlocked = true;
                        _em.SetComponentData(pe, pSeg);
                    }

                    // The existing belt points into the new cell — block the existing belt's output.
                    int2 qExitCell = AdjacentCell(q, qSeg.ExitDir);
                    if (qExitCell.x == p.x && qExitCell.y == p.y && !qSeg.OutputBlocked)
                    {
                        qSeg.OutputBlocked = true;
                        _em.SetComponentData(qe, qSeg);
                    }
                }
            }
        }

        /// <summary>
        /// Removes the conveyor segment at (x, y), if one exists, then rebuilds connectivity so the
        /// remaining segments re-derive their tails/heads (inbounds of the removed cell become
        /// tails; its successor becomes a head only if it has no other inbound). Returns true if a
        /// segment was removed. Single source of truth for conveyor removal — shared by
        /// DeconstructController and the placement controller's Destroy mode.
        /// </summary>
        public bool RemoveSegmentAt(int x, int y)
        {
            if (!_queryReady) return false;

            var entities = _segmentQuery.ToEntityArray(Allocator.Temp);
            Entity target = Entity.Null;
            for (int i = 0; i < entities.Length; i++)
            {
                var s = _em.GetComponentData<ConveyorSegmentData>(entities[i]);
                if (s.Cell.x == x && s.Cell.y == y) { target = entities[i]; break; }
            }
            entities.Dispose();

            if (target == Entity.Null) return false;

            _em.DestroyEntity(target);
            GridOccupancy.Instance?.UnregisterConveyor(x, y);
            conveyorVisualizer?.RemoveBelt(x, y);

            RebuildTopology();
            conveyorVisualizer?.RefreshAll();

            GameLogger.Develop($"[ConveyorPlacer] Removed conveyor segment at ({x},{y}).");
            return true;
        }

        // ----------------------------------------------------------------
        // Topology — derive all links from per-cell ExitDir geometry
        // ----------------------------------------------------------------

        private void RebuildTopology()
        {
            RelinkAll();
            TryAutoTurnEnds();
        }

        /// <summary>
        /// Rebuilds NextSegment / PrevSegment / EntryDir / IsChainHead for every segment from each
        /// cell's ExitDir, via the pure <see cref="ConveyorTopology"/> solver. A cell outputs to the
        /// segment in its ExitDir neighbour (if any); its inbounds are the segments whose ExitDir
        /// points back at it. A cell with no inbound is a chain head. The primary (first) inbound
        /// drives EntryDir and PrevSegment; heads default to EntryDir == ExitDir (straight) until
        /// auto-turn adjusts them.
        /// </summary>
        private void RelinkAll()
        {
            if (!_queryReady) return;

            var entities = _segmentQuery.ToEntityArray(Allocator.Temp);
            var cellMap  = new Dictionary<int2, Entity>(entities.Length);
            var data     = new Dictionary<Entity, ConveyorSegmentData>(entities.Length);
            var cellExit = new Dictionary<int2, int>(entities.Length);
            var blocked  = new HashSet<int2>();

            for (int i = 0; i < entities.Length; i++)
            {
                var s = _em.GetComponentData<ConveyorSegmentData>(entities[i]);
                cellMap[s.Cell]   = entities[i];
                data[entities[i]] = s;
                cellExit[s.Cell]  = s.ExitDir;
                if (s.OutputBlocked) blocked.Add(s.Cell);
            }

            var links = ConveyorTopology.Compute(cellExit, blocked);

            foreach (var kv in data)
            {
                Entity e = kv.Key;
                var    s = kv.Value;
                var    l = links[s.Cell];

                s.NextSegment = (l.HasOutput && cellMap.TryGetValue(l.OutputCell, out var ne)) ? ne : Entity.Null;

                if (l.InboundCount > MaxInbounds)
                    GameLogger.Warning($"[ConveyorPlacer] Cell {s.Cell} has {l.InboundCount} inbound " +
                        $"conveyors (>{MaxInbounds}); merge cap exceeded.");

                if (l.IsHead)
                {
                    s.IsChainHead = true;
                    s.PrevSegment = Entity.Null;
                    s.EntryDir    = s.ExitDir;             // straight by default
                }
                else
                {
                    s.IsChainHead = false;
                    s.PrevSegment = (l.HasPrimaryInbound && cellMap.TryGetValue(l.PrimaryInboundCell, out var pe))
                        ? pe : Entity.Null;
                    s.EntryDir    = l.PrimaryInboundExitDir; // travel dir of the primary inbound
                }

                _em.SetComponentData(e, s);
            }

            entities.Dispose();
        }

        /// <summary>
        /// Orients dead-end cells toward adjacent building ports so the flow always looks natural:
        ///   - a TAIL (no conveyor successor) whose neighbour is a building Input facing it turns its
        ///     ExitDir toward that input (90° if the flow was perpendicular);
        ///   - a HEAD whose neighbour is a building Output facing it sets its EntryDir from that side
        ///     so the current visually emerges from the building.
        /// The matching convention mirrors ConveyorSystem (input Facing == approach dir; output
        /// Facing == OppositeDir of the head's approach). Deterministic, so it re-runs identically on
        /// load. Must run AFTER RelinkAll (it only adjusts ExitDir/EntryDir, never the links).
        /// </summary>
        private void TryAutoTurnEnds()
        {
            if (!_queryReady) return;

            var bldgEntities = _buildingQuery.ToEntityArray(Allocator.Temp);
            if (bldgEntities.Length == 0) { bldgEntities.Dispose(); return; }

            var buildingMap = new Dictionary<int2, Entity>(bldgEntities.Length * 2);
            for (int i = 0; i < bldgEntities.Length; i++)
            {
                var pos = _em.GetComponentData<GridPosition>(bldgEntities[i]).Cell;
                buildingMap[pos] = bldgEntities[i];
                if (_em.HasComponent<BuildingFootprint>(bldgEntities[i]))
                {
                    var fp = _em.GetComponentData<BuildingFootprint>(bldgEntities[i]);
                    for (int dx = 0; dx < fp.Width;  dx++)
                    for (int dy = 0; dy < fp.Height; dy++)
                        buildingMap[new int2(pos.x + dx, pos.y + dy)] = bldgEntities[i];
                }
            }
            bldgEntities.Dispose();

            var segEntities = _segmentQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < segEntities.Length; i++)
            {
                var e = segEntities[i];
                var s = _em.GetComponentData<ConveyorSegmentData>(e);
                int2 cell = s.Cell;
                bool changed = false;

                // Tail: turn output toward an adjacent building input facing this cell.
                if (s.NextSegment == Entity.Null)
                {
                    int newExit = ConveyorAutoTurn.TailExit(s.ExitDir, dir =>
                    {
                        int2 cand = AdjacentCell(cell, dir);
                        return buildingMap.TryGetValue(cand, out var b) && HasMatchingPort(b, cand, dir, PortType.Input);
                    });
                    if (newExit != s.ExitDir) { s.ExitDir = newExit; changed = true; }
                }

                // Head: receive flow from an adjacent building output facing this cell.
                if (s.IsChainHead)
                {
                    int newEntry = ConveyorAutoTurn.HeadEntry(s.EntryDir, dir =>
                    {
                        int2 cand = AdjacentCell(cell, dir);
                        return buildingMap.TryGetValue(cand, out var b) && HasMatchingPort(b, cand, OppositeDir(dir), PortType.Output);
                    });
                    if (newEntry != s.EntryDir) { s.EntryDir = newEntry; changed = true; }
                }

                if (changed) _em.SetComponentData(e, s);
            }
            segEntities.Dispose();
        }

        private bool HasMatchingPort(Entity building, int2 portCell, int dir, PortType required)
        {
            if (!_em.HasBuffer<PlacedPortData>(building)) return false;
            var ports = _em.GetBuffer<PlacedPortData>(building, isReadOnly: true);
            for (int i = 0; i < ports.Length; i++)
            {
                var p = ports[i];
                if (p.PortType != (int)required) continue;
                if (p.CellX != portCell.x || p.CellY != portCell.y) continue;
                if (p.Facing == dir) return true;
            }
            return false;
        }

        // ----------------------------------------------------------------
        // Helpers
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

        private static int2 AdjacentCell(int2 cell, int dir)
        {
            return (OutputDirection)dir switch
            {
                OutputDirection.North => new int2(cell.x,     cell.y + 1),
                OutputDirection.East  => new int2(cell.x + 1, cell.y    ),
                OutputDirection.South => new int2(cell.x,     cell.y - 1),
                OutputDirection.West  => new int2(cell.x - 1, cell.y    ),
                _                    => cell
            };
        }

        private static int OppositeDir(int dir) => (dir + 2) % 4;
    }
}
