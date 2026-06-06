using System;
using System.Collections.Generic;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Walks serialised GridSaveData to find active idle chains without requiring ECS or Unity runtime.
    ///
    /// An idle chain: a collector building whose output-port cell is directly upstream of a
    /// conveyor chain that terminates at either an entropy-sink building (items auto-sell) or
    /// no named building (items go to player inventory).
    ///
    /// Recipe-based synthesisers mid-chain are NOT simulated — only pure collector output is tracked.
    /// </summary>
    public static class IdleGraphAnalyzer
    {
        /// <summary>
        /// Builds a fresh IdleCollectionSnapshot from the current grid save state.
        /// All building metadata is provided via delegates so this class stays Unity-runtime-free
        /// and fully testable in EditMode.
        /// </summary>
        /// <param name="grid">The saved grid to analyse.</param>
        /// <param name="isCollector">Returns true if the building (by buildingId) is a field-collector (PlacementRule.MustBeOnField).</param>
        /// <param name="isEntropySink">Returns true if the building (by buildingId) is an entropy sink (Maxwell's Demon).</param>
        /// <param name="getFootprint">Returns the grid footprint (width × height) for a buildingId.</param>
        /// <param name="getOutputItemId">Returns the output item ID for (buildingId, recipeId). -1 if unknown.</param>
        /// <param name="getOutputRate">Returns items-per-second for a buildingId (base rate only; caller applies any multipliers before storing).</param>
        /// <param name="getItemSellValue">Returns baseSellValue for an itemId.</param>
        /// <param name="effectiveItemsPerSecond">Multiplier applied to each chain's rate (e.g. prestige speed multiplier).</param>
        /// <param name="snapshotTimestamp">ISO 8601 UTC string to stamp on the snapshot.</param>
        public static IdleCollectionSnapshot BuildSnapshot(
            GridSaveData grid,
            Func<int, bool>   isCollector,
            Func<int, bool>   isEntropySink,
            Func<int, Vector2Int> getFootprint,
            Func<int, int, int>   getOutputItemId,
            Func<int, float>      getOutputRate,
            Func<int, float>      getItemSellValue,
            float effectiveItemsPerSecondMultiplier,
            string snapshotTimestamp)
        {
            var snapshot = new IdleCollectionSnapshot
            {
                snapshotTimestampUtc = snapshotTimestamp
            };

            if (grid == null) return snapshot;

            var buildings   = grid.buildings  ?? new List<BuildingSaveData>();
            var conveyors   = grid.conveyors  ?? new List<ConveyorSaveData>();

            // Build a map: every cell covered by a building → that building
            var cellToBuilding = BuildCellMap(buildings, getFootprint);

            foreach (var chain in conveyors)
            {
                if (chain?.cells == null || chain.cells.Length < 4) continue;

                // Head = where items enter the belt; tail = where items exit
                int hx = chain.cells[0], hy = chain.cells[1];
                int nx = chain.cells[2], ny = chain.cells[3];

                int flowDx = nx - hx;
                int flowDy = ny - hy;

                // Building that feeds the head is one step upstream
                var upstreamCell = new Vector2Int(hx - flowDx, hy - flowDy);
                if (!cellToBuilding.TryGetValue(upstreamCell, out var srcBuilding)) continue;
                if (!isCollector(srcBuilding.buildingId)) continue;

                // Building that receives from the tail is one step downstream
                int tx = chain.cells[chain.cells.Length - 2];
                int ty = chain.cells[chain.cells.Length - 1];

                int lastDx, lastDy;
                if (chain.cells.Length >= 4)
                {
                    int ptx = chain.cells[chain.cells.Length - 4];
                    int pty = chain.cells[chain.cells.Length - 3];
                    lastDx = tx - ptx;
                    lastDy = ty - pty;
                }
                else
                {
                    lastDx = flowDx;
                    lastDy = flowDy;
                }

                var downstreamCell = new Vector2Int(tx + lastDx, ty + lastDy);
                cellToBuilding.TryGetValue(downstreamCell, out var dstBuilding);
                bool endsAtSink = dstBuilding != null && isEntropySink(dstBuilding.buildingId);

                int itemId = getOutputItemId(srcBuilding.buildingId, srcBuilding.recipeId);
                if (itemId < 0) continue;

                float rate = getOutputRate(srcBuilding.buildingId) * effectiveItemsPerSecondMultiplier;
                if (rate <= 0f) continue;

                snapshot.chains.Add(new IdleChainEntry
                {
                    itemId           = itemId,
                    itemsPerSecond   = rate,
                    endsAtEntropySink = endsAtSink,
                    baseSellValue    = getItemSellValue(itemId)
                });
            }

            return snapshot;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static Dictionary<Vector2Int, BuildingSaveData> BuildCellMap(
            List<BuildingSaveData> buildings,
            Func<int, Vector2Int> getFootprint)
        {
            var map = new Dictionary<Vector2Int, BuildingSaveData>();
            foreach (var bsd in buildings)
            {
                if (bsd?.position == null || bsd.position.Length < 2) continue;
                int ax = bsd.position[0], ay = bsd.position[1];
                Vector2Int fp = getFootprint(bsd.buildingId);
                int w = Mathf.Max(1, fp.x);
                int h = Mathf.Max(1, fp.y);
                for (int dx = 0; dx < w; dx++)
                for (int dy = 0; dy < h; dy++)
                    map[new Vector2Int(ax + dx, ay + dy)] = bsd;
            }
            return map;
        }
    }
}
