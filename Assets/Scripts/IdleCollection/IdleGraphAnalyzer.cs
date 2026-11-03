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
        public float ByproductEntropy;   // entropy paid per craft by escaping byproducts (neutrinos, gamma)
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
            public bool  IsFilter;           // #321: passes items through by its rules; never a final consumer
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
            Func<int, int, IdleRecipeInfo> getRecipeInfo = null,
            float surplusSaleFraction = 0f,
            Func<int, bool> isFilter = null)
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
                    IsFilter    = isFilter?.Invoke(bsd.buildingId) ?? false,
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
                return nodes[idx].IsSink || nodes[idx].IsFilter ||
                       (nodes[idx].Recipe.Valid && nodes[idx].Recipe.InputItemIds != null &&
                        nodes[idx].Recipe.InputItemIds.Length > 0);
            }

            var cellToBuilding = BuildCellMap(buildings, getFootprint);
            var filterExits    = new List<(int filter, int side, int to)>();

            // ---- Edges (producer at head → consumer/inventory at tail) --------
            foreach (var chain in conveyors)
            {
                if (chain?.cells == null || chain.cells.Length < 2) continue;

                int hx = chain.cells[0], hy = chain.cells[1];
                int tx = chain.cells[chain.cells.Length - 2];
                int ty = chain.cells[chain.cells.Length - 1];

                var consumer = FindAdjacent(tx, ty, cellToBuilding, CanConsume);
                int to = consumer != null ? indexOf[consumer] : -1;

                var producer = FindAdjacent(hx, hy, cellToBuilding, CanProduce);
                if (producer == null)
                {
                    // A belt leaving a Filter: remember which side it starts from and where it ends.
                    var filter = FindAdjacent(hx, hy, cellToBuilding, b => nodes[indexOf[b]].IsFilter);
                    if (filter != null && filter.position?.Length >= 2)
                    {
                        int f = indexOf[filter];
                        int side = SideFacing(filter.position[0], filter.position[1], hx, hy);
                        if (side >= 0 && to != f) filterExits.Add((f, side, to));
                    }
                    continue;
                }

                int from = indexOf[producer];
                if (to == from) continue; // ignore a belt that loops back to its own source

                nodes[from].Outgoing.Add(to);
                if (to >= 0) nodes[to].Incoming.Add(from);
            }

            // ---- Filters are see-through (#321) --------------------------------
            // A belt into a Filter is re-pointed at wherever the Filter sends that producer's item (following
            // chained Filters). An item no side takes waits on its belt, so that edge earns nothing.
            for (int i = 0; i < n; i++)
            {
                var node = nodes[i];
                if (node.IsFilter) continue;
                int item = StaticOutputItem(node, getOutputItemId);
                for (int k = node.Outgoing.Count - 1; k >= 0; k--)
                {
                    int to = node.Outgoing[k];
                    if (to < 0 || !nodes[to].IsFilter) continue;
                    nodes[to].Incoming.Remove(i);
                    int target = ThroughFilters(to, item, nodes, filterExits);
                    if (target == Waits || target == i) { node.Outgoing.RemoveAt(k); continue; }
                    node.Outgoing[k] = target;
                    if (target >= 0) nodes[target].Incoming.Add(i);
                }
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
            //   • into a STARVED synth   → the item is stuck (the synth can't process it). With a
            //                              surplusSaleFraction it is sold at that share of its price, as live
            //                              play sells unusable items (#312); with 0 it goes to inventory (the
            //                              old non-regressive fallback). Either way no output is dropped.
            for (int i = 0; i < n; i++)
            {
                var node = nodes[i];
                if (node.OutputItemId < 0 || node.OutputRate <= 0f || node.OutgoingCount == 0) continue;

                float perEdge = node.OutputRate / node.OutgoingCount;
                if (perEdge <= 0f) continue;

                foreach (int to in node.Outgoing)
                {
                    bool sink         = to >= 0 && nodes[to].IsSink;
                    bool producingSyn = to >= 0 && nodes[to].Recipe.Valid && !nodes[to].IsSink
                                                && nodes[to].OutputRate > 0f;
                    if (producingSyn) continue; // consumed by a synth that will be credited via its own output

                    bool stuck = to >= 0 && !sink && surplusSaleFraction > 0f;
                    float price = getItemSellValue(node.OutputItemId);
                    snapshot.chains.Add(new IdleChainEntry
                    {
                        itemId            = node.OutputItemId,
                        itemsPerSecond    = perEdge,
                        endsAtEntropySink = sink || stuck,
                        baseSellValue     = stuck ? price * surplusSaleFraction : price
                    });
                }
            }

            // ---- Escaping byproducts → entropy, once per craft of every producing synth -----------
            // Paid regardless of where the main output goes, exactly like the live ProductionSystem.
            for (int i = 0; i < n; i++)
            {
                var node = nodes[i];
                if (!node.Recipe.Valid || node.IsSink || node.Recipe.ByproductEntropy <= 0f) continue;
                if (node.OutputRate <= 0f) continue;

                int outQty = node.Recipe.OutputQuantity > 0 ? node.Recipe.OutputQuantity : 1;
                snapshot.chains.Add(new IdleChainEntry
                {
                    itemId            = node.OutputItemId,
                    itemsPerSecond    = node.OutputRate / outQty,   // crafts per second
                    endsAtEntropySink = true,
                    baseSellValue     = node.Recipe.ByproductEntropy
                });
            }

            GameLogger.Debug($"[IdleGraph] buildings={n} conveyors={conveyors.Count} chains={snapshot.chains.Count}");
            return snapshot;
        }

        private const int Waits = -2;

        /// <summary>The item a producer always makes, known before rates are computed (collector or recipe).</summary>
        private static int StaticOutputItem(Node node, Func<int, int, int> getOutputItemId)
        {
            if (node.IsCollector) return getOutputItemId(node.Bsd.buildingId, node.Bsd.recipeId);
            return node.Recipe.Valid ? node.Recipe.OutputItemId : -1;
        }

        /// <summary>
        /// Where <paramref name="item"/> ends up after entering Filter <paramref name="filter"/>: a consumer node,
        /// -1 (a belt to nowhere: inventory), or <see cref="Waits"/> when no side takes it or that side has no belt.
        /// A Filter with no rules (an old save) sends everything out of its first belt.
        /// </summary>
        private static int ThroughFilters(int filter, int item, Node[] nodes, List<(int filter, int side, int to)> exits)
        {
            for (int hop = 0; hop < 8; hop++)
            {
                var rules = PortFilterRules.Parse(nodes[filter].Bsd.portFilters);
                int side  = rules.Count == 0 ? FirstExitSide(filter, exits) : PortFilterRules.Route(rules, item);
                if (side == PortFilterRules.NoRoute) return Waits;

                int next = Waits;
                foreach (var e in exits)
                    if (e.filter == filter && e.side == side) { next = e.to; break; }
                if (next == Waits || next < 0 || !nodes[next].IsFilter) return next;
                filter = next;
            }
            return Waits; // a loop of Filters
        }

        private static int FirstExitSide(int filter, List<(int filter, int side, int to)> exits)
        {
            foreach (var e in exits) if (e.filter == filter) return e.side;
            return PortFilterRules.NoRoute;
        }

        /// <summary>OutputDirection from a 1x1 building at (fx, fy) to the neighbouring cell (hx, hy), or -1.</summary>
        private static int SideFacing(int fx, int fy, int hx, int hy)
        {
            int dx = hx - fx, dy = hy - fy;
            if (dx == 0 && dy == 1)  return (int)OutputDirection.North;
            if (dx == 1 && dy == 0)  return (int)OutputDirection.East;
            if (dx == 0 && dy == -1) return (int)OutputDirection.South;
            if (dx == -1 && dy == 0) return (int)OutputDirection.West;
            return -1;
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
                // Rotation 1/3 swaps width and height — a vertical Strong Force Combiner covers (x,y+1),
                // not (x+1,y), and belts touching its real second cell must still connect.
                Vector2Int fp = PortUtils.RotatedFootprint(getFootprint(bsd.buildingId), bsd.rotation);
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
