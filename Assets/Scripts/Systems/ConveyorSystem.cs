using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Simulates item movement along conveyor belt chains.
    ///
    /// Runs entirely on the main thread (no Burst, no jobs) using direct EntityManager access.
    /// This avoids safety-handle conflicts that arise from calling EntityManager inside
    /// Entities.ForEach lambdas.
    ///
    /// Four passes per frame:
    ///   1. Build cell→entity lookup maps for segments and buildings.
    ///   2. Advance ConveyorItemData.Progress on every occupied segment.
    ///   3. Transfer items: segment→next-segment or segment→building input buffer.
    ///   4. Pull items from building output buffers onto chain-head segments.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProductionSystem))]
    public partial class ConveyorSystem : SystemBase
    {
        private EntityQuery _segmentQuery;
        private EntityQuery _buildingQuery;
        private float       _debugTimer;

        protected override void OnCreate()
        {
            RequireForUpdate<ConveyorSegmentData>();

            _segmentQuery  = GetEntityQuery(typeof(ConveyorSegmentData));
            _buildingQuery = GetEntityQuery(typeof(BuildingData), typeof(GridPosition));
        }

        protected override void OnUpdate()
        {
            float dt = SystemAPI.Time.DeltaTime;
            _debugTimer += dt;
            bool debugLog = _debugTimer >= 2f;
            if (debugLog) _debugTimer = 0f;

            // ----------------------------------------------------------------
            // Pass 1 — Build cell→entity lookup maps
            // ----------------------------------------------------------------
            var segEntities  = _segmentQuery.ToEntityArray(Allocator.Temp);
            var bldgEntities = _buildingQuery.ToEntityArray(Allocator.Temp);

            var segMap      = new NativeHashMap<int2, Entity>(segEntities.Length  * 2, Allocator.Temp);
            var buildingMap = new NativeHashMap<int2, Entity>(bldgEntities.Length * 4, Allocator.Temp);

            for (int i = 0; i < segEntities.Length; i++)
            {
                var seg = EntityManager.GetComponentData<ConveyorSegmentData>(segEntities[i]);
                segMap.TryAdd(seg.Cell, segEntities[i]);
            }

            for (int i = 0; i < bldgEntities.Length; i++)
            {
                var pos = EntityManager.GetComponentData<GridPosition>(bldgEntities[i]);
                buildingMap.TryAdd(pos.Cell, bldgEntities[i]);

                if (EntityManager.HasComponent<BuildingFootprint>(bldgEntities[i]))
                {
                    var fp = EntityManager.GetComponentData<BuildingFootprint>(bldgEntities[i]);
                    for (int dx = 0; dx < fp.Width;  dx++)
                    for (int dy = 0; dy < fp.Height; dy++)
                        buildingMap.TryAdd(new int2(pos.Cell.x + dx, pos.Cell.y + dy), bldgEntities[i]);
                }
            }

            // ----------------------------------------------------------------
            // Pass 2 — Advance item progress (back-pressure aware)
            //
            // Items advance freely up to 0.5 (visual centre of their segment).
            // Past 0.5 they only continue if the next step is clear; otherwise
            // they stop at 0.5 so queued items rest in the middle of each cell
            // rather than all piling at the exit edge of the last segment.
            // ----------------------------------------------------------------
            for (int i = 0; i < segEntities.Length; i++)
            {
                var e = segEntities[i];
                if (!EntityManager.HasComponent<ConveyorItemData>(e)) continue;

                var seg  = EntityManager.GetComponentData<ConveyorSegmentData>(e);
                var item = EntityManager.GetComponentData<ConveyorItemData>(e);

                float newProg = item.Progress + dt / seg.TransportTime;

                if (newProg >= 0.5f)
                {
                    bool canAdvance;

                    if (seg.NextSegment != Entity.Null)
                    {
                        // Mid-chain: clear if the next segment is empty
                        canAdvance = !EntityManager.HasComponent<ConveyorItemData>(seg.NextSegment);
                    }
                    else
                    {
                        // Tail: clear if an accepting building input is in any adjacent cell that
                        // sits in front of the input port — regardless of the belt's exit direction.
                        canAdvance = false;
                        if (TryFindInputBuilding(seg.Cell, buildingMap, out Entity destBldg, out _))
                        {
                            var inputBuf  = EntityManager.GetBuffer<BuildingInputSlot>(destBldg);
                            var invConfig = EntityManager.GetComponentData<BuildingInventoryConfig>(destBldg);
                            canAdvance = SlotBufferUtils.TotalInInputBuffer(inputBuf) < invConfig.InputCapacity;
                        }
                    }

                    if (!canAdvance)
                        newProg = Mathf.Min(newProg, 0.5f);
                }

                item.Progress = Mathf.Min(newProg, 1f);
                EntityManager.SetComponentData(e, item);
            }

            // ----------------------------------------------------------------
            // Pass 3 — Transfer items (deferred, applied after collection)
            // ----------------------------------------------------------------
            var transfers = new List<(Entity from, Entity toSeg, Entity toBuilding, int itemID, float newProg, int deliverDir)>();

            for (int i = 0; i < segEntities.Length; i++)
            {
                var e = segEntities[i];
                if (!EntityManager.HasComponent<ConveyorItemData>(e)) continue;

                var seg  = EntityManager.GetComponentData<ConveyorSegmentData>(e);
                var item = EntityManager.GetComponentData<ConveyorItemData>(e);
                if (item.Progress < 1f) continue;

                if (seg.NextSegment != Entity.Null)
                {
                    // Move to next segment if it is empty
                    if (!EntityManager.HasComponent<ConveyorItemData>(seg.NextSegment))
                    {
                        transfers.Add((e, seg.NextSegment, Entity.Null, item.ItemID, item.Progress - 1f, -1));
                    }
                    // else: back-pressure — item waits
                }
                else
                {
                    // Tail segment — deposit into a building input whose port faces this cell,
                    // in whichever adjacent direction it sits (independent of the belt's exit dir).
                    if (TryFindInputBuilding(seg.Cell, buildingMap, out Entity bldg, out int deliverDir))
                    {
                        var inputBuf  = EntityManager.GetBuffer<BuildingInputSlot>(bldg);
                        var invConfig = EntityManager.GetComponentData<BuildingInventoryConfig>(bldg);
                        if (SlotBufferUtils.TotalInInputBuffer(inputBuf) < invConfig.InputCapacity)
                            transfers.Add((e, Entity.Null, bldg, item.ItemID, 0f, deliverDir));
                        // else: input full — item waits
                    }
                    // No adjacent input port: item waits at tail
                }
            }

            foreach (var (from, toSeg, toBuilding, itemID, newProg, deliverDir) in transfers)
            {
                if (toBuilding != Entity.Null)
                {
                    // Deposit to building input buffer
                    SlotBufferUtils.AddToInputBuffer(EntityManager.GetBuffer<BuildingInputSlot>(toBuilding), itemID, 1);
                    EntityManager.RemoveComponent<ConveyorItemData>(from);

                    // Input particle burst toward the building's entry face (the delivery direction,
                    // which may differ from the belt's own exit direction).
                    if (BuildingInputFX.Instance != null)
                    {
                        var seg = EntityManager.GetComponentData<ConveyorSegmentData>(from);
                        BuildingInputFX.Instance.Trigger(seg.Cell, deliverDir, itemID);
                    }
                }
                else if (toSeg != Entity.Null && !EntityManager.HasComponent<ConveyorItemData>(toSeg))
                {
                    EntityManager.AddComponentData(toSeg, new ConveyorItemData
                    {
                        ItemID   = itemID,
                        Progress = Mathf.Max(0f, newProg)
                    });
                    EntityManager.RemoveComponent<ConveyorItemData>(from);
                }
            }

            // ----------------------------------------------------------------
            // Pass 4 — Pull items from building output buffers onto chain heads
            // ----------------------------------------------------------------
            var pulls = new List<(Entity head, int itemID)>();

            for (int i = 0; i < segEntities.Length; i++)
            {
                var e   = segEntities[i];
                var seg = EntityManager.GetComponentData<ConveyorSegmentData>(e);
                if (!seg.IsChainHead) continue;
                if (EntityManager.HasComponent<ConveyorItemData>(e)) continue; // occupied

                // Scan all 4 neighbours for a building whose output port faces this segment.
                // We cannot rely solely on EntryDir because the belt's travel direction may be
                // perpendicular to the direction the connected building outputs toward us.
                Entity bldg       = Entity.Null;
                int2   sourceCell = default;
                int    portFacing = -1; // the facing stored on the port (direction from bldg toward belt)

                for (int dir = 0; dir < 4; dir++)
                {
                    int2 candidate = AdjacentCell(seg.Cell, dir);
                    if (!buildingMap.TryGetValue(candidate, out Entity candidateBldg)) continue;

                    // Port must face from the building toward this chain head, i.e. OppositeDir(dir)
                    int facing = OppositeDir(dir);
                    if (!HasMatchingPort(candidateBldg, candidate, facing, PortType.Output)) continue;

                    bldg       = candidateBldg;
                    sourceCell = candidate;
                    portFacing = facing;
                    break;
                }

                if (bldg == Entity.Null)
                {
                    if (debugLog)
                    {
                        // Collect neighbour info for diagnosis
                        string neighbourInfo = "";
                        for (int dir = 0; dir < 4; dir++)
                        {
                            int2 candidate = AdjacentCell(seg.Cell, dir);
                            if (!buildingMap.TryGetValue(candidate, out Entity nb)) continue;
                            string portDump = "no buffer";
                            if (EntityManager.HasBuffer<PlacedPortData>(nb))
                            {
                                var pb = EntityManager.GetBuffer<PlacedPortData>(nb, isReadOnly: true);
                                portDump = pb.Length == 0 ? "empty" : "";
                                for (int p = 0; p < pb.Length; p++)
                                    portDump += $"[t={pb[p].PortType} ({pb[p].CellX},{pb[p].CellY}) f={pb[p].Facing}]";
                            }
                            neighbourInfo += $"\n    dir={dir} cell={candidate} ports={portDump}";
                        }
                        GameLogger.Develop($"[ConveyorSystem] Chain head @ {seg.Cell} (EntryDir={seg.EntryDir}): " +
                                  $"no adjacent building with a matching Output port.{neighbourInfo}");
                    }
                    continue;
                }

                var outputBuf = EntityManager.GetBuffer<BuildingOutputSlot>(bldg);
                if (outputBuf.Length == 0)
                {
                    if (debugLog)
                        GameLogger.Develop($"[ConveyorSystem] Chain head @ {seg.Cell}: building at {sourceCell} " +
                                  $"output buffer is empty (collector not yet producing?)");
                    continue;
                }

                int itemID = outputBuf[0].ItemID;
                SlotBufferUtils.RemoveFromOutputBuffer(outputBuf, itemID, 1);
                pulls.Add((e, itemID));
                if (debugLog)
                    GameLogger.Develop($"[ConveyorSystem] Chain head @ {seg.Cell}: pulled itemID={itemID} " +
                              $"from building at {sourceCell} (portFacing={portFacing}) onto belt.");
            }

            foreach (var (head, itemID) in pulls)
            {
                if (!EntityManager.HasComponent<ConveyorItemData>(head))
                {
                    EntityManager.AddComponentData(head, new ConveyorItemData
                    {
                        ItemID   = itemID,
                        Progress = 0f
                    });
                }
            }

            segEntities.Dispose();
            bldgEntities.Dispose();
            segMap.Dispose();
            buildingMap.Dispose();
        }

        // ----------------------------------------------------------------
        // Port-matching
        // ----------------------------------------------------------------

        /// <summary>
        /// Returns true if building has a port of the required type at portCell facing in dir.
        /// Output ports store the facing they exit toward.
        /// Input ports store the facing of the direction they accept from (same convention).
        /// </summary>
        /// <summary>
        /// Scans the four cells around <paramref name="segCell"/> for a building whose Input port
        /// faces this cell. An input port stores its Facing as the inbound flow direction, so the
        /// belt cell must sit in front of it — i.e. for neighbour direction <c>dir</c> the port's
        /// Facing equals <c>dir</c>. Lets a belt feed an input regardless of its own exit direction,
        /// as long as the tail rests on the cell in front of the input. Returns the first match.
        /// </summary>
        private bool TryFindInputBuilding(int2 segCell, NativeHashMap<int2, Entity> buildingMap,
                                          out Entity building, out int deliverDir)
        {
            for (int dir = 0; dir < 4; dir++)
            {
                int2 candidate = AdjacentCell(segCell, dir);
                if (!buildingMap.TryGetValue(candidate, out Entity cand)) continue;
                if (!HasMatchingPort(cand, candidate, dir, PortType.Input)) continue;

                building   = cand;
                deliverDir = dir;
                return true;
            }

            building   = Entity.Null;
            deliverDir = -1;
            return false;
        }

        private bool HasMatchingPort(Entity building, int2 portCell, int dir, PortType required)
        {
            if (!EntityManager.HasBuffer<PlacedPortData>(building)) return false;
            var ports = EntityManager.GetBuffer<PlacedPortData>(building, isReadOnly: true);
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
        // Grid helpers
        // ----------------------------------------------------------------

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
