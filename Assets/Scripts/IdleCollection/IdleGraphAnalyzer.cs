using System;
using System.Collections.Generic;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Walks serialised GridSaveData to find active idle chains without requiring ECS or Unity runtime.
    ///
    /// An idle chain: a collector building adjacent to the HEAD of a conveyor chain that
    /// terminates at either an entropy-sink building (items auto-sell) or no named building
    /// (items go to player inventory).  Source/sink buildings may feed/receive from any side
    /// of the HEAD/TAIL cell, not just the direction the belt is flowing.
    ///
    /// Recipe-based synthesisers mid-chain are NOT simulated — only pure collector output is tracked.
    /// </summary>
    public static class IdleGraphAnalyzer
    {
        private static readonly int[] AdjacentDirs = { -1, 0, 1, 0, 0, -1, 0, 1 };

        public static IdleCollectionSnapshot BuildSnapshot(
            GridSaveData grid,
            Func<int, bool>       isCollector,
            Func<int, bool>       isEntropySink,
            Func<int, Vector2Int> getFootprint,
            Func<int, int, int>   getOutputItemId,
            Func<int, float>      getOutputRate,
            Func<int, float>      getItemSellValue,
            float effectiveItemsPerSecondMultiplier,
            string snapshotTimestamp,
            Func<BuildingSaveData, float> getManagerOutputMultiplier = null)
        {
            var snapshot = new IdleCollectionSnapshot
            {
                snapshotTimestampUtc = snapshotTimestamp
            };

            if (grid == null) return snapshot;

            var buildings = grid.buildings ?? new List<BuildingSaveData>();
            var conveyors = grid.conveyors ?? new List<ConveyorSaveData>();

            var cellToBuilding = BuildCellMap(buildings, getFootprint);

            foreach (var chain in conveyors)
            {
                if (chain?.cells == null || chain.cells.Length < 2) continue;

                int hx = chain.cells[0], hy = chain.cells[1];
                int tx = chain.cells[chain.cells.Length - 2];
                int ty = chain.cells[chain.cells.Length - 1];

                // Source: any collector adjacent to HEAD (can feed from any side)
                var srcBuilding = FindAdjacent(hx, hy, cellToBuilding, isCollector);
                if (srcBuilding == null)
                {
                    GameLogger.Debug($"[IdleGraph] Chain skipped — no collector adjacent to head=({hx},{hy}) cellsLen={chain.cells.Length}");
                    continue;
                }

                // Sink: any entropy-sink adjacent to TAIL (can receive from any side)
                var dstBuilding = FindAdjacent(tx, ty, cellToBuilding, isEntropySink);
                bool endsAtSink = dstBuilding != null;

                int itemId = getOutputItemId(srcBuilding.buildingId, srcBuilding.recipeId);
                if (itemId < 0)
                {
                    GameLogger.Debug($"[IdleGraph] Chain skipped — itemId=-1 for buildingId={srcBuilding.buildingId} recipeId={srcBuilding.recipeId}");
                    continue;
                }

                // A manager assigned to the source collector multiplies its offline throughput
                // (OutputQuantity), matching CollectorSystem's live per-interval deposit. 1 otherwise.
                float mgrMult = getManagerOutputMultiplier?.Invoke(srcBuilding) ?? 1f;
                float rate = getOutputRate(srcBuilding.buildingId) * effectiveItemsPerSecondMultiplier * mgrMult;
                if (rate <= 0f)
                {
                    GameLogger.Debug($"[IdleGraph] Chain skipped — rate={rate} for buildingId={srcBuilding.buildingId}");
                    continue;
                }

                GameLogger.Debug($"[IdleGraph] Chain added — src=b{srcBuilding.buildingId} itemId={itemId} rate={rate} sink={endsAtSink} head=({hx},{hy}) tail=({tx},{ty})");

                snapshot.chains.Add(new IdleChainEntry
                {
                    itemId            = itemId,
                    itemsPerSecond    = rate,
                    endsAtEntropySink = endsAtSink,
                    baseSellValue     = getItemSellValue(itemId)
                });
            }

            return snapshot;
        }

        private static BuildingSaveData FindAdjacent(
            int cx, int cy,
            Dictionary<Vector2Int, BuildingSaveData> cellMap,
            Func<int, bool> predicate)
        {
            for (int d = 0; d < 4; d++)
            {
                int nx = cx + AdjacentDirs[d * 2];
                int ny = cy + AdjacentDirs[d * 2 + 1];
                if (cellMap.TryGetValue(new Vector2Int(nx, ny), out var bsd) && predicate(bsd.buildingId))
                    return bsd;
            }
            return null;
        }

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
