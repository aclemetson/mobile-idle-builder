using System;
using System.Collections.Generic;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>Recipe data an offline synthesizer node needs, resolved by the caller from the building's
    /// current recipe. Kept as plain data so <see cref="IdleGraphAnalyzer"/> stays testable without ECS/SO.</summary>
    public struct IdleRecipeInfo
    {
        public bool  Valid;
        public int   OutputItemId;
        public int   OutputQuantity;
        public float CraftTimeSeconds;
        public int[] InputItemIds;
        public int[] InputQuantities;
    }

    /// <summary>
    /// Walks serialised GridSaveData to model offline production as a directed graph, without ECS or the
    /// Unity runtime.
    ///
    /// Nodes are buildings; a conveyor is an edge from the building adjacent to its HEAD (the producer) to the
    /// building adjacent to its TAIL (the consumer, or nothing → player inventory). Collectors are sources
    /// (produce their field item at a fixed rate). Recipe buildings ("synthesizers") are input-limited: their
    /// offline output rate is the smaller of their craft rate and what their incoming item flow can sustain,
    /// so a chain like collector → combiner → Maxwell's Demon credits the SYNTHESISED item's value (e.g.
    /// hydrogen) at the sink — bounded by the real upstream collector throughput, so it can never over-credit.
    /// Entropy sinks convert their incoming item flow to currency; terminal flows to no building go to inventory.
    ///
    /// When <paramref name="getRecipeInfo"/> is null the graph degenerates to the original model (only pure
    /// collector output reaches sinks/inventory; synthesizers are treated as non-producing).
    /// </summary>
    public static class IdleGraphAnalyzer
    {
        private static readonly int[] AdjacentDirs = { -1, 0, 1, 0, 0, -1, 0, 1 };

        private sealed class Node
        {
            public BuildingSaveData Bsd;
            public bool  IsCollector;
            public bool  IsSink;
            public IdleRecipeInfo Recipe;   // valid only for synthesizers
            public readonly List<int> Outgoing = new(); // indices (or -1 for inventory)
            public readonly List<int> Incoming = new(); // producer node indices feeding this node
            public int   OutputItemId = -1;
            public float OutputRate;         // items/sec (speed + manager scaled; pre idle-collection-rate)
            public bool  Computed;
            public int   OutgoingCount => Outgoing.Count;
        }

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
            Func<BuildingSaveData, float> getManagerOutputMultiplier = null,
            Func<int, int, IdleRecipeInfo> getRecipeInfo = null)
        {
            var snapshot = new IdleCollectionSnapshot { snapshotTimestampUtc = snapshotTimestamp };
            if (grid == null) return snapshot;

            var buildings = grid.buildings ?? new List<BuildingSaveData>();
            var conveyors = grid.conveyors ?? new List<ConveyorSaveData>();

            // ---- Nodes ---------------------------------------------------------
            int n = buildings.Count;
            var nodes   = new Node[n];
            var indexOf = new Dictionary<BuildingSaveData, int>(n);
            for (int i = 0; i < n; i++)
            {
                var bsd  = buildings[i];
                var info = getRecipeInfo?.Invoke(bsd.buildingId, bsd.recipeId) ?? default;
                nodes[i] = new Node
                {
                    Bsd         = bsd,
                    IsCollector = isCollector(bsd.buildingId),
                    IsSink      = isEntropySink(bsd.buildingId),
                    Recipe      = info
                };
                indexOf[bsd] = i;
            }

            bool CanProduce(BuildingSaveData b)
            {
                int idx = indexOf[b];
                return nodes[idx].IsCollector || (nodes[idx].Recipe.Valid && !nodes[idx].IsSink);
            }
            bool CanConsume(BuildingSaveData b)
            {
                int idx = indexOf[b];
                return nodes[idx].IsSink ||
                       (nodes[idx].Recipe.Valid && nodes[idx].Recipe.InputItemIds != null &&
                        nodes[idx].Recipe.InputItemIds.Length > 0);
            }

            var cellToBuilding = BuildCellMap(buildings, getFootprint);

            // ---- Edges (producer at head → consumer/inventory at tail) --------
            foreach (var chain in conveyors)
            {
                if (chain?.cells == null || chain.cells.Length < 2) continue;

                int hx = chain.cells[0], hy = chain.cells[1];
                int tx = chain.cells[chain.cells.Length - 2];
                int ty = chain.cells[chain.cells.Length - 1];

                var producer = FindAdjacent(hx, hy, cellToBuilding, CanProduce);
                if (producer == null) continue;

                var consumer = FindAdjacent(tx, ty, cellToBuilding, CanConsume);
                int from = indexOf[producer];
                int to   = consumer != null ? indexOf[consumer] : -1;
                if (to == from) continue; // ignore a belt that loops back to its own source

                nodes[from].Outgoing.Add(to);
                if (to >= 0) nodes[to].Incoming.Add(from);
            }

            // ---- Compute output rates (collectors first, synthesizers by flood) -
            float eff = effectiveItemsPerSecondMultiplier;
            float Mgr(Node node) => getManagerOutputMultiplier?.Invoke(node.Bsd) ?? 1f;

            var queue   = new Queue<int>();
            var pending = new int[n]; // for synthesizers: # of incoming producer edges not yet resolved

            for (int i = 0; i < n; i++)
            {
                var node = nodes[i];
                if (node.IsCollector)
                {
                    int itemId = getOutputItemId(node.Bsd.buildingId, node.Bsd.recipeId);
                    float rate = getOutputRate(node.Bsd.buildingId) * eff * Mgr(node);
                    node.OutputItemId = itemId;
                    node.OutputRate   = (itemId >= 0 && rate > 0f) ? rate : 0f;
                    node.Computed     = true;
                    queue.Enqueue(i);
                }
                else if (node.Recipe.Valid && !node.IsSink)
                {
                    pending[i] = node.Incoming.Count;
                    if (pending[i] == 0) { ComputeSynth(node, nodes, eff, Mgr); node.Computed = true; queue.Enqueue(i); }
                }
                else
                {
                    // Sink or non-producing building: nothing to compute, but still propagate order.
                    node.Computed = true;
                    queue.Enqueue(i);
                }
            }

            while (queue.Count > 0)
            {
                var u = nodes[queue.Dequeue()];
                foreach (int v in u.Outgoing)
                {
                    if (v < 0) continue;
                    var vn = nodes[v];
                    if (vn.Computed || !(vn.Recipe.Valid && !vn.IsSink)) continue;
                    if (--pending[v] > 0) continue;
                    ComputeSynth(vn, nodes, eff, Mgr);
                    vn.Computed = true;
                    queue.Enqueue(v);
                }
            }

            // ---- Terminal flows → chain entries --------------------------------
            // Each producing node's output is credited exactly once, split across its outgoing belts:
            //   • into a sink            → entropy from the item's sell value;
            //   • into no building       → player inventory;
            //   • into a PRODUCING synth → consumed internally (credited later as that synth's output);
            //   • into a STARVED synth   → the item is stuck (the synth can't process it), so credit it to
            //                              inventory — this is the non-regressive fallback that guarantees no
            //                              producer output is ever silently dropped (matches the old model
            //                              when a whole chain fails to resolve).
            for (int i = 0; i < n; i++)
            {
                var node = nodes[i];
                if (node.OutputItemId < 0 || node.OutputRate <= 0f || node.OutgoingCount == 0) continue;

                float perEdge = node.OutputRate / node.OutgoingCount;
                if (perEdge <= 0f) continue;

                foreach (int to in node.Outgoing)
                {
                    bool sink         = to >= 0 && nodes[to].IsSink;
                    bool inventory    = to < 0;
                    bool producingSyn = to >= 0 && nodes[to].Recipe.Valid && !nodes[to].IsSink
                                                && nodes[to].OutputRate > 0f;
                    if (producingSyn) continue; // consumed by a synth that will be credited via its own output

                    snapshot.chains.Add(new IdleChainEntry
                    {
                        itemId            = node.OutputItemId,
                        itemsPerSecond    = perEdge,
                        endsAtEntropySink = sink,
                        baseSellValue     = getItemSellValue(node.OutputItemId)
                    });
                }
            }

            GameLogger.Debug($"[IdleGraph] buildings={n} conveyors={conveyors.Count} chains={snapshot.chains.Count}");
            return snapshot;
        }

        /// <summary>Input-limited output for a synthesizer node: min(craft rate, what its inputs sustain).</summary>
        private static void ComputeSynth(Node node, Node[] nodes, float eff, Func<Node, float> mgr)
        {
            var r = node.Recipe;
            if (!r.Valid || r.OutputItemId < 0) { node.OutputItemId = -1; node.OutputRate = 0f; return; }

            float craftsPerSec = r.CraftTimeSeconds > 0f ? eff / r.CraftTimeSeconds : 0f;

            int inputs = r.InputItemIds?.Length ?? 0;
            for (int k = 0; k < inputs; k++)
            {
                int   inItem = r.InputItemIds[k];
                int   inQty  = r.InputQuantities != null && k < r.InputQuantities.Length ? r.InputQuantities[k] : 1;
                if (inQty <= 0) continue; // no requirement for this slot

                // Rate of this input item arriving from all upstream producers (each split across its outputs).
                float arriving = 0f;
                foreach (int u in node.Incoming)
                {
                    var un = nodes[u];
                    if (un.OutputItemId == inItem && un.OutputRate > 0f && un.OutgoingCount > 0)
                        arriving += un.OutputRate / un.OutgoingCount;
                }

                float craftsFromInput = arriving / inQty;
                if (craftsFromInput < craftsPerSec) craftsPerSec = craftsFromInput;
            }

            int outQty = r.OutputQuantity > 0 ? r.OutputQuantity : 1;
            node.OutputItemId = r.OutputItemId;
            node.OutputRate   = craftsPerSec > 0f ? craftsPerSec * outQty * mgr(node) : 0f;
        }

        private static BuildingSaveData FindAdjacent(
            int cx, int cy,
            Dictionary<Vector2Int, BuildingSaveData> cellMap,
            Func<BuildingSaveData, bool> predicate)
        {
            for (int d = 0; d < 4; d++)
            {
                int nx = cx + AdjacentDirs[d * 2];
                int ny = cy + AdjacentDirs[d * 2 + 1];
                if (cellMap.TryGetValue(new Vector2Int(nx, ny), out var bsd) && predicate(bsd))
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
