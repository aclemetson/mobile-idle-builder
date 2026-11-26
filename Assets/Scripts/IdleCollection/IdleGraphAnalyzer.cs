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
            public bool  IsRouter => IsFilter;
            public IdleRecipeInfo Recipe;   // valid only for synthesizers
            public readonly List<int>   Outgoing   = new(); // indices (or -1 for inventory)
            public readonly List<float> OutWeights = new(); // share of the output each Outgoing entry takes (belt splits)
            public readonly List<int>   Incoming   = new(); // producer node indices feeding this node (one per edge)

            public float TotalOutWeight
            {
                get { float t = 0f; foreach (var w in OutWeights) t += w; return t; }
            }

            /// <summary>Items/s of this node's output that reach node <paramref name="to"/> over all its edges there.</summary>
            public float FlowTo(int to)
            {
                float total = TotalOutWeight, share = 0f;
                for (int k = 0; k < Outgoing.Count; k++) if (Outgoing[k] == to) share += OutWeights[k];
                return total > 0f ? OutputRate * share / total : 0f;
            }

            public void AddEdge(int to, float weight, Node[] all, int self)
            {
                Outgoing.Add(to);
                OutWeights.Add(weight);
                if (to >= 0) all[to].Incoming.Add(self);
            }
            public int   OutputItemId = -1;
            public float OutputRate;         // items/sec (speed + manager scaled; pre idle-collection-rate)
            public float CraftsPerSec;       // synthesizers: steady-state crafts/sec (input-limited)
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
            Func<int, bool> isFilter = null,
            Func<BuildingSaveData, float> getCollectorRate = null)
        {
            var snapshot = new IdleCollectionSnapshot { snapshotTimestampUtc = snapshotTimestamp };
            if (grid == null) return snapshot;

            // Belt Splitter buildings became belt splits (2026-11-24); an old grid is converted first.
            SplitterMigration.Convert(grid);
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
                return nodes[idx].IsSink || nodes[idx].IsRouter ||
                       (nodes[idx].Recipe.Valid && nodes[idx].Recipe.InputItemIds != null &&
                        nodes[idx].Recipe.InputItemIds.Length > 0);
            }

            var cellToBuilding = BuildCellMap(buildings, getFootprint);
            var filterExits    = new List<(int filter, int side, int to, float weight)>();

            // Belt splits: where each split sits, and every chain by its first cell (a split's branches are saved
            // as chains starting on the tile beside it).
            var splitMap    = new Dictionary<Vector2Int, BeltSplitData>();
            foreach (var sd in BeltSplitRules.Parse(grid.splits)) splitMap[new Vector2Int(sd.X, sd.Y)] = sd;
            var chainByHead = new Dictionary<Vector2Int, ConveyorSaveData>();
            foreach (var chain in conveyors)
                if (chain?.cells != null && chain.cells.Length >= 2)
                {
                    var head = new Vector2Int(chain.cells[0], chain.cells[1]);
                    if (!chainByHead.ContainsKey(head)) chainByHead[head] = chain;
                }

            // Where one chain's items end up: the building at its tail, shared out at every split on the way.
            void Route(ConveyorSaveData chain, float weight, List<(int to, float w)> into, int depth)
            {
                int len = chain.cells.Length / 2;
                float w = weight;
                for (int c = 0; c < len && w > 0f; c++)
                {
                    var cell = new Vector2Int(chain.cells[c * 2], chain.cells[c * 2 + 1]);
                    if (!splitMap.TryGetValue(cell, out var sp)) continue;
                    int next = c < len - 1
                        ? DirBetween(cell, new Vector2Int(chain.cells[c * 2 + 2], chain.cells[c * 2 + 3])) : -1;
                    float total = 0f;
                    for (int d = 0; d < 4; d++)
                        if (BeltSplitRules.Has(sp.Mask, d) && (d == next || chainByHead.ContainsKey(Step(cell, d))))
                            total += BeltSplitRules.ClampWeight(sp.Weight(d));
                    if (total <= 0f) continue;
                    float trunk = 0f;
                    for (int d = 0; d < 4; d++)
                    {
                        if (!BeltSplitRules.Has(sp.Mask, d)) continue;
                        float share = w * BeltSplitRules.ClampWeight(sp.Weight(d)) / total;
                        if (d == next) trunk = share;
                        else if (depth < 16 && chainByHead.TryGetValue(Step(cell, d), out var branch))
                            Route(branch, share, into, depth + 1);
                    }
                    w = trunk;
                }
                if (w <= 0f) return;
                int tx = chain.cells[chain.cells.Length - 2], ty = chain.cells[chain.cells.Length - 1];
                var consumer = FindAdjacent(tx, ty, cellToBuilding, CanConsume);
                into.Add((consumer != null ? indexOf[consumer] : -1, w));
            }

            bool FedBySplit(Vector2Int cell)
            {
                for (int d = 0; d < 4; d++)
                    if (splitMap.TryGetValue(Step(cell, d), out var sp) && BeltSplitRules.Has(sp.Mask, (d + 2) % 4))
                        return true;
                return false;
            }

            // ---- Edges (producer at head → consumer/inventory at tail, shared at splits) --------
            var routed = new List<(int to, float w)>();
            foreach (var chain in conveyors)
            {
                if (chain?.cells == null || chain.cells.Length < 2) continue;

                int hx = chain.cells[0], hy = chain.cells[1];
                if (FedBySplit(new Vector2Int(hx, hy))) continue;   // a split's branch: reached through the split

                routed.Clear();
                Route(chain, 1f, routed, 0);

                var producer = FindAdjacent(hx, hy, cellToBuilding, CanProduce);
                if (producer == null)
                {
                    // A belt leaving a Filter: remember which side it starts from and where it ends.
                    var filter = FindAdjacent(hx, hy, cellToBuilding, b => nodes[indexOf[b]].IsRouter);
                    if (filter != null && filter.position?.Length >= 2)
                    {
                        int f = indexOf[filter];
                        int side = SideFacing(filter.position[0], filter.position[1], hx, hy);
                        if (side >= 0)
                            foreach (var (to, w) in routed) if (to != f) filterExits.Add((f, side, to, w));
                    }
                    continue;
                }

                int from = indexOf[producer];
                foreach (var (to, w) in routed)
                    if (to != from) nodes[from].AddEdge(to, w, nodes, from);   // a belt looping back to its source is ignored
            }

            // ---- Filters are see-through (#321) ---------------------------------
            // A belt into a Filter is re-pointed at wherever the Filter sends that producer's item (following
            // chained Filters), keeping the belt's share. An item no side takes waits on its belt: that edge earns nothing.
            for (int i = 0; i < n; i++)
            {
                var node = nodes[i];
                if (node.IsRouter) continue;
                int item = StaticOutputItem(node, getOutputItemId);
                for (int k = node.Outgoing.Count - 1; k >= 0; k--)
                {
                    int to = node.Outgoing[k];
                    if (to < 0 || !nodes[to].IsRouter) continue;
                    float edgeWeight = node.OutWeights[k];
                    nodes[to].Incoming.Remove(i);
                    node.Outgoing.RemoveAt(k);
                    node.OutWeights.RemoveAt(k);

                    var targets = new List<(int to, float w)>();
                    ThroughRouters(to, item, nodes, filterExits, targets, 1f, 0);
                    foreach (var (target, w) in targets)
                        if (target != i) node.AddEdge(target, edgeWeight * w, nodes, i);
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
                    // Per building when the caller can (speed level), else the type's base rate.
                    float baseRate = getCollectorRate != null ? getCollectorRate(node.Bsd)
                                                              : getOutputRate(node.Bsd.buildingId);
                    float rate = baseRate * eff * Mgr(node);
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
                float totalWeight = node.TotalOutWeight;
                if (totalWeight <= 0f) continue;

                for (int k = 0; k < node.Outgoing.Count; k++)
                {
                    int   to      = node.Outgoing[k];
                    float perEdge = node.OutputRate * node.OutWeights[k] / totalWeight;
                    if (perEdge <= 0f) continue;
                    bool sink         = to >= 0 && nodes[to].IsSink;
                    bool producingSyn = to >= 0 && nodes[to].Recipe.Valid && !nodes[to].IsSink
                                                && nodes[to].OutputRate > 0f;
                    if (producingSyn)
                    {
                        // Consumed by a synth credited via its own output, except what its recipe can't use at
                        // steady state: live play sells that at a full input (full-input autosell), so offline does too.
                        float excess = surplusSaleFraction > 0f
                            ? ExcessInto(nodes[to], node.OutputItemId, perEdge, nodes) : 0f;
                        if (excess > 1e-6f)
                            snapshot.chains.Add(new IdleChainEntry
                            {
                                itemId            = node.OutputItemId,
                                itemsPerSecond    = excess,
                                endsAtEntropySink = true,
                                baseSellValue     = getItemSellValue(node.OutputItemId) * surplusSaleFraction,
                                notDelivered      = true   // sold at the full input, never reaches the Demon
                            });
                        continue;
                    }

                    bool stuck = to >= 0 && !sink && surplusSaleFraction > 0f;
                    float price = getItemSellValue(node.OutputItemId);
                    snapshot.chains.Add(new IdleChainEntry
                    {
                        itemId            = node.OutputItemId,
                        itemsPerSecond    = perEdge,
                        endsAtEntropySink = sink || stuck,
                        baseSellValue     = stuck ? price * surplusSaleFraction : price,
                        notDelivered      = stuck            // a surplus sale, not a Demon delivery
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
                    baseSellValue     = node.Recipe.ByproductEntropy,
                    notDelivered      = true   // escape entropy per craft; the item itself goes wherever its belt goes
                });
            }

            GameLogger.Debug($"[IdleGraph] buildings={n} conveyors={conveyors.Count} chains={snapshot.chains.Count}");
            return snapshot;
        }

        /// <summary>The item a producer always makes, known before rates are computed (collector or recipe).</summary>
        private static int StaticOutputItem(Node node, Func<int, int, int> getOutputItemId)
        {
            if (node.IsCollector) return getOutputItemId(node.Bsd.buildingId, node.Bsd.recipeId);
            return node.Recipe.Valid ? node.Recipe.OutputItemId : -1;
        }

        /// <summary>
        /// Every place <paramref name="item"/> ends up after entering Filter or Splitter <paramref name="router"/>,
        /// one entry per belt it is shared over (consumer node, or -1 for inventory). Nothing is added where it
        /// waits. A Filter picks one side by its rules (with no rules, an old save, its first belt); a Splitter uses all of its belts.
        /// </summary>
        private static void ThroughRouters(int router, int item, Node[] nodes,
                                           List<(int filter, int side, int to, float weight)> exits,
                                           List<(int to, float w)> targets, float share, int depth)
        {
            if (depth >= 8) return; // a loop of Filters

            var rules = PortFilterRules.Parse(nodes[router].Bsd.portFilters);
            int side  = rules.Count == 0 ? FirstExitSide(router, exits) : PortFilterRules.Route(rules, item);
            if (side == PortFilterRules.NoRoute) return;
            // The belt on that side may split on its way: each of its ends takes its share.
            foreach (var e in exits)
            {
                if (e.filter != router || e.side != side) continue;
                if (e.to >= 0 && nodes[e.to].IsRouter) ThroughRouters(e.to, item, nodes, exits, targets, share * e.weight, depth + 1);
                else targets.Add((e.to, share * e.weight));
            }
        }

        private static int FirstExitSide(int filter, List<(int filter, int side, int to, float weight)> exits)
        {
            foreach (var e in exits) if (e.filter == filter) return e.side;
            return PortFilterRules.NoRoute;
        }

        /// <summary>The cell one step from <paramref name="c"/> in OutputDirection <paramref name="d"/>.</summary>
        private static Vector2Int Step(Vector2Int c, int d) => d switch
        {
            0 => new Vector2Int(c.x, c.y + 1),
            1 => new Vector2Int(c.x + 1, c.y),
            2 => new Vector2Int(c.x, c.y - 1),
            _ => new Vector2Int(c.x - 1, c.y),
        };

        /// <summary>OutputDirection of the step between two adjacent cells.</summary>
        private static int DirBetween(Vector2Int a, Vector2Int b) =>
            b.y > a.y ? 0 : b.x > a.x ? 1 : b.y < a.y ? 2 : 3;

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

        /// <summary>
        /// The part of <paramref name="perEdge"/> (one belt's flow of <paramref name="itemId"/> into synth
        /// <paramref name="synth"/>) that the synth can't use at steady state. 0 for an item its recipe doesn't use.
        /// When several belts bring the same item, the excess is shared in proportion to their flow.
        /// </summary>
        private static float ExcessInto(Node synth, int itemId, float perEdge, Node[] nodes)
        {
            var r = synth.Recipe;
            int qty = 0;
            for (int k = 0; k < (r.InputItemIds?.Length ?? 0); k++)
                if (r.InputItemIds[k] == itemId)
                    qty = r.InputQuantities != null && k < r.InputQuantities.Length ? r.InputQuantities[k] : 1;
            if (qty <= 0) return 0f;

            float arriving = 0f;
            int self = Array.IndexOf(nodes, synth);
            foreach (int u in new HashSet<int>(synth.Incoming))
            {
                var un = nodes[u];
                if (un.OutputItemId == itemId && un.OutputRate > 0f && un.OutgoingCount > 0)
                    arriving += un.FlowTo(self);
            }
            if (arriving <= 0f) return 0f;
            float excess = arriving - synth.CraftsPerSec * qty;
            return excess > 0f ? excess * (perEdge / arriving) : 0f;
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
                int self = Array.IndexOf(nodes, node);
                foreach (int u in new HashSet<int>(node.Incoming))
                {
                    var un = nodes[u];
                    if (un.OutputItemId == inItem && un.OutputRate > 0f && un.OutgoingCount > 0)
                        arriving += un.FlowTo(self);
                }

                float craftsFromInput = arriving / inQty;
                if (craftsFromInput < craftsPerSec) craftsPerSec = craftsFromInput;
            }

            int outQty = r.OutputQuantity > 0 ? r.OutputQuantity : 1;
            node.OutputItemId = r.OutputItemId;
            node.CraftsPerSec = craftsPerSec > 0f ? craftsPerSec : 0f;
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
