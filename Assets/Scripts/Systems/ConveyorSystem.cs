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

        protected override void OnCreate()
        {
            RequireForUpdate<ConveyorSegmentData>();

            _segmentQuery  = GetEntityQuery(typeof(ConveyorSegmentData));
            _buildingQuery = GetEntityQuery(typeof(BuildingData), typeof(GridPosition));
        }

        protected override void OnUpdate()
        {
            float dt = SystemAPI.Time.DeltaTime;

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
            // Pass 2 — Advance item progress
            // ----------------------------------------------------------------
            for (int i = 0; i < segEntities.Length; i++)
            {
                var e = segEntities[i];
                if (!EntityManager.HasComponent<ConveyorItemData>(e)) continue;

                var seg  = EntityManager.GetComponentData<ConveyorSegmentData>(e);
                var item = EntityManager.GetComponentData<ConveyorItemData>(e);
                item.Progress += dt / seg.TransportTime;
                EntityManager.SetComponentData(e, item);
            }

            // ----------------------------------------------------------------
            // Pass 3 — Transfer items (deferred, applied after collection)
            // ----------------------------------------------------------------
            var transfers = new List<(Entity from, Entity toSeg, Entity toBuilding, int itemID, float newProg)>();

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
                        transfers.Add((e, seg.NextSegment, Entity.Null, item.ItemID, item.Progress - 1f));
                    }
                    // else: back-pressure — item waits
                }
                else
                {
                    // Tail segment — try to deposit into an adjacent building input port
                    int2 destCell = AdjacentCell(seg.Cell, seg.ExitDir);

                    if (buildingMap.TryGetValue(destCell, out Entity bldg) &&
                        HasMatchingPort(bldg, destCell, seg.ExitDir, PortType.Input))
                    {
                        var inputBuf  = EntityManager.GetBuffer<BuildingInputSlot>(bldg);
                        var invConfig = EntityManager.GetComponentData<BuildingInventoryConfig>(bldg);
                        if (TotalInInputBuffer(inputBuf) < invConfig.InputCapacity)
                            transfers.Add((e, Entity.Null, bldg, item.ItemID, 0f));
                        // else: input full — item waits
                    }
                    // No building or port: item waits at tail
                }
            }

            foreach (var (from, toSeg, toBuilding, itemID, newProg) in transfers)
            {
                if (toBuilding != Entity.Null)
                {
                    // Deposit to building input buffer
                    AddToInputBuffer(EntityManager.GetBuffer<BuildingInputSlot>(toBuilding), itemID, 1);
                    EntityManager.RemoveComponent<ConveyorItemData>(from);
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

                // The source building cell is adjacent in the opposite of the entry direction
                int2 sourceCell = AdjacentCell(seg.Cell, OppositeDir(seg.EntryDir));
                if (!buildingMap.TryGetValue(sourceCell, out Entity bldg)) continue;

                // Building must have an output port facing in the entry direction (toward the belt)
                if (!HasMatchingPort(bldg, sourceCell, seg.EntryDir, PortType.Output)) continue;

                var outputBuf = EntityManager.GetBuffer<BuildingOutputSlot>(bldg);
                if (outputBuf.Length == 0) continue;

                int itemID = outputBuf[0].ItemID;
                RemoveFromOutputBuffer(outputBuf, itemID, 1);
                pulls.Add((e, itemID));
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
        // Buffer helpers
        // ----------------------------------------------------------------

        private static int TotalInInputBuffer(DynamicBuffer<BuildingInputSlot> buf)
        {
            int n = 0;
            for (int i = 0; i < buf.Length; i++) n += buf[i].Quantity;
            return n;
        }

        private static void AddToInputBuffer(DynamicBuffer<BuildingInputSlot> buf, int itemID, int qty)
        {
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i].ItemID != itemID) continue;
                buf[i] = new BuildingInputSlot { ItemID = itemID, Quantity = buf[i].Quantity + qty };
                return;
            }
            buf.Add(new BuildingInputSlot { ItemID = itemID, Quantity = qty });
        }

        private static void RemoveFromOutputBuffer(DynamicBuffer<BuildingOutputSlot> buf, int itemID, int qty)
        {
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i].ItemID != itemID) continue;
                int rem = buf[i].Quantity - qty;
                if (rem <= 0) buf.RemoveAt(i);
                else buf[i] = new BuildingOutputSlot { ItemID = itemID, Quantity = rem };
                return;
            }
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
