using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Re-seeds restored conveyor chains with in-transit items on load, so belts appear full and
    /// flowing on the first frame instead of visibly re-filling from the harvesters. This is the
    /// visual half of the "kept running while you were away" illusion; the economic half is granted
    /// separately by <see cref="OfflineCollectionService"/> + <see cref="IdleGraphAnalyzer"/>.
    ///
    /// Presentation only: primed items are ordinary cargo — they flow to their sink/inventory through
    /// the normal <c>ConveyorSystem</c> path. Their count is bounded by belt length (at most
    /// <see cref="BeltSettings.ItemsPerTile"/> per cell), so any entropy they add at the sink is negligible
    /// next to the offline-earnings payout.
    ///
    /// Called once per cold-boot load from <c>GridSaveService.LoadGrid()</c> after conveyors are
    /// restored. Background→foreground does not reload the grid (belts stay live), so there is no
    /// re-prime path and no double-count.
    /// </summary>
    public static class BeltPrimer
    {
        /// <summary>
        /// Pure spacing core (no ECS): where primed items go so the belt reads at the feeding producer's
        /// steady-state density. Returns (segment index head = 0 .. tail = chainLength-1, Progress) pairs,
        /// head first. Items sit one producer interval apart along the belt, but never closer than one
        /// slot (1 / itemsPerTile tiles), which is how tightly the live belt packs. A gap of a tile or more is
        /// rounded to whole tiles with each item mid-tile, as a one-item-per-tile belt rests. Empty when the
        /// chain is degenerate or nothing is being produced.
        /// </summary>
        public static List<(int cell, float progress)> ComputeItemPositions(int chainLength, float effectiveRatePerSec,
                                                                            float transportTime, int itemsPerTile)
        {
            var positions = new List<(int, float)>();
            if (chainLength <= 0 || effectiveRatePerSec <= 0f || transportTime <= 0f)
                return positions;

            // The producer deposits one item every `interval` seconds; in that time an item travels
            // `interval / transportTime` tiles. That gap, never under one slot, is the steady-state spacing.
            float slot  = BeltSettings.Spacing(itemsPerTile);
            float gap   = (1f / effectiveRatePerSec) / transportTime;
            gap = gap >= 1f ? Mathf.Max(1, Mathf.RoundToInt(gap)) : Mathf.Max(slot, gap);

            // Each item sits mid-way through its own gap (mid-tile when the gap is a whole tile).
            float first = Mathf.Min(gap, 1f) * 0.5f;
            for (int k = 0; ; k++)
            {
                float x = first + k * gap;
                int cell = Mathf.FloorToInt(x + 1e-4f);
                if (cell >= chainLength) break;
                positions.Add((cell, Mathf.Clamp01(x - cell)));
            }
            return positions;
        }

        /// <summary>
        /// Resolves a chain-feeding building (by its baked id + active recipe) to the item it outputs
        /// and its effective output rate (items/sec). Returns null when the building produces nothing
        /// routable onto a belt (no recipe/output item), so those chains are left unprimed. Every
        /// producer — field collectors and synthesisers/processors alike — is a valid prime source,
        /// since priming is presentation only and has no bearing on offline earnings.
        /// </summary>
        public delegate (int itemId, float ratePerSec)? CollectorOutputResolver(int buildingId, int recipeId);

        /// <summary>
        /// Walks every restored conveyor chain and puts <see cref="ConveyorItem"/>s on its cells
        /// at steady-state spacing. Returns the number of items placed. Never throws: a null resolver,
        /// empty queries, or a chain with no collector simply yields no priming.
        /// </summary>
        /// <param name="globalRateMultiplier">Prestige speed × megastructure speed × premium boost —
        /// folded into every chain's rate so spacing matches the live sim's effective throughput.</param>
        public static int PrimeAllChains(EntityManager em,
                                         EntityQuery segmentQuery,
                                         EntityQuery buildingQuery,
                                         CollectorOutputResolver resolve,
                                         float globalRateMultiplier)
        {
            if (resolve == null) return 0;
            if (globalRateMultiplier <= 0f) globalRateMultiplier = 1f;

            // Research belt-speed bonus (ResearchEffects singleton): items cross a cell faster, so
            // the effective transport time shrinks. 1 when the singleton is absent in this world.
            float beltSpeedMult = 1f;
            var effectsQuery = em.CreateEntityQuery(ComponentType.ReadOnly<ResearchEffects>());
            if (effectsQuery.HasSingleton<ResearchEffects>())
                beltSpeedMult = effectsQuery.GetSingleton<ResearchEffects>().ConveyorSpeedMult;
            effectsQuery.Dispose();
            if (beltSpeedMult <= 0f) beltSpeedMult = 1f;

            int itemsPerTile = BeltSettings.DefaultItemsPerTile;
            var beltQuery = em.CreateEntityQuery(ComponentType.ReadOnly<BeltSettings>());
            if (beltQuery.HasSingleton<BeltSettings>())
                itemsPerTile = BeltSettings.Clamp(beltQuery.GetSingleton<BeltSettings>().ItemsPerTile);
            beltQuery.Dispose();

            var segEntities  = segmentQuery.ToEntityArray(Allocator.Temp);
            var bldgEntities = buildingQuery.ToEntityArray(Allocator.Temp);

            var segMap      = new Dictionary<int2, Entity>(segEntities.Length);
            var buildingMap = new Dictionary<int2, Entity>(bldgEntities.Length * 2);

            for (int i = 0; i < segEntities.Length; i++)
            {
                var seg = em.GetComponentData<ConveyorSegmentData>(segEntities[i]);
                segMap[seg.Cell] = segEntities[i];
            }

            for (int i = 0; i < bldgEntities.Length; i++)
            {
                var pos = em.GetComponentData<GridPosition>(bldgEntities[i]);
                buildingMap[pos.Cell] = bldgEntities[i];

                if (em.HasComponent<BuildingFootprint>(bldgEntities[i]))
                {
                    var fp = em.GetComponentData<BuildingFootprint>(bldgEntities[i]);
                    for (int dx = 0; dx < fp.Width;  dx++)
                    for (int dy = 0; dy < fp.Height; dy++)
                        buildingMap[new int2(pos.Cell.x + dx, pos.Cell.y + dy)] = bldgEntities[i];
                }
            }

            int placed = 0;
            // Diagnostic funnel counters: where priming drops chains tells us what's misconfigured
            // (0 heads → topology not linked; 0 fed → port mismatch; 0 resolved → recipe/rate; etc.).
            int nHeads = 0, nFed = 0, nResolved = 0, nChains = 0;

            for (int i = 0; i < segEntities.Length; i++)
            {
                var head    = segEntities[i];
                var headSeg = em.GetComponentData<ConveyorSegmentData>(head);
                if (!headSeg.IsChainHead) continue;
                nHeads++;

                // The chain is fed by a building whose Output port faces the head cell (mirrors the
                // pull logic in ConveyorSystem Pass 4 — EntryDir alone is not reliable).
                if (!TryFindFeedingBuilding(em, headSeg.Cell, buildingMap, out Entity bldg)) continue;
                nFed++;

                int buildingId = em.GetComponentData<BuildingData>(bldg).BuildingType;
                int recipeId   = em.HasComponent<RecipeProcessData>(bldg)
                               ? em.GetComponentData<RecipeProcessData>(bldg).RecipeID
                               : -1;

                var output = resolve(buildingId, recipeId);
                if (output == null || output.Value.itemId < 0) continue;   // not a collector we prime
                nResolved++;

                float rate = output.Value.ratePerSec * globalRateMultiplier;
                if (rate <= 0f) continue;
                nChains++;

                // Collect the chain's segment entities head → tail.
                var chain = new List<Entity>();
                var cur   = head;
                int guard = 0;
                while (cur != Entity.Null && em.HasComponent<ConveyorSegmentData>(cur) && guard++ < 10000)
                {
                    chain.Add(cur);
                    cur = em.GetComponentData<ConveyorSegmentData>(cur).NextSegment;
                }

                float transportTime = headSeg.TransportTime > 0f ? headSeg.TransportTime : 1f;
                transportTime /= beltSpeedMult;
                var positions = ComputeItemPositions(chain.Count, rate, transportTime, itemsPerTile);

                foreach (var (idx, progress) in positions)
                {
                    var cell = chain[idx];
                    if (ConveyorItems.Count(em, cell) >= itemsPerTile) continue;   // tile already full
                    ConveyorItems.Add(em, cell, output.Value.itemId, progress);
                    placed++;
                }
            }

            segEntities.Dispose();
            bldgEntities.Dispose();

            GameLogger.Debug($"[Prime] segments={segMap.Count} chainHeads={nHeads} fed={nFed} " +
                             $"resolved={nResolved} primedChains={nChains} itemsPlaced={placed} " +
                             $"(globalRateMult={globalRateMultiplier:F2})");
            return placed;
        }

        // ── Feeding-building / port matching (parallels ConveyorSystem) ──────────

        static bool TryFindFeedingBuilding(EntityManager em, int2 headCell,
                                           Dictionary<int2, Entity> buildingMap, out Entity building)
        {
            for (int dir = 0; dir < 4; dir++)
            {
                int2 neighbour = AdjacentCell(headCell, dir);
                if (!buildingMap.TryGetValue(neighbour, out Entity cand)) continue;

                // The port must face from the building toward the head cell, i.e. OppositeDir(dir).
                if (HasOutputPortFacing(em, cand, neighbour, OppositeDir(dir)))
                {
                    building = cand;
                    return true;
                }
            }
            building = Entity.Null;
            return false;
        }

        static bool HasOutputPortFacing(EntityManager em, Entity building, int2 portCell, int facing)
        {
            if (!em.HasBuffer<PlacedPortData>(building)) return false;
            var ports = em.GetBuffer<PlacedPortData>(building, isReadOnly: true);
            for (int i = 0; i < ports.Length; i++)
            {
                var p = ports[i];
                if (p.PortType != (int)PortType.Output) continue;
                if (p.CellX != portCell.x || p.CellY != portCell.y) continue;
                if (p.Facing == facing) return true;
            }
            return false;
        }

        static int2 AdjacentCell(int2 cell, int dir) => (OutputDirection)dir switch
        {
            OutputDirection.North => new int2(cell.x,     cell.y + 1),
            OutputDirection.East  => new int2(cell.x + 1, cell.y    ),
            OutputDirection.South => new int2(cell.x,     cell.y - 1),
            OutputDirection.West  => new int2(cell.x - 1, cell.y    ),
            _                     => cell
        };

        static int OppositeDir(int dir) => (dir + 2) % 4;
    }
}
