using System;
using System.Collections.Generic;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Offline production for a site in a stream world (Chemistry), the counterpart of
    /// <see cref="IdleGraphAnalyzer"/>, which models belts as producer-to-consumer edges. A stream is one
    /// ordered channel instead: items flow from the Spring down to the mouth and every building on the banks
    /// takes what its recipe uses and adds its product further down. Same rules as live play:
    /// <list type="bullet">
    /// <item>Flow is shared upstream first (<see cref="StreamRules.Take"/>); a building runs at its share.</item>
    /// <item>A collector (Extractor) adds its item; a recipe building crafts at
    /// min(craft rate x share, what the items upstream of it sustain), takes those inputs out and adds its product.</item>
    /// <item>At the mouth a sink (Calorimeter) pays full value; an open mouth sells at the surplus fraction.</item>
    /// <item>A dry channel (no Spring) produces nothing.</item>
    /// </list>
    /// Buildings are matched to the first channel cell beside their footprint (ports are not saved offline,
    /// the same approximation IdleGraphAnalyzer uses). Pure: no ECS, no ScriptableObjects.
    /// </summary>
    public static class StreamIdleAnalyzer
    {
        static readonly Vector2Int[] Dirs = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };

        public static IdleCollectionSnapshot BuildSnapshot(
            GridSaveData grid,
            Func<int, bool>       isCollector,
            Func<int, bool>       isEntropySink,
            Func<int, Vector2Int> getFootprint,
            Func<int, int, int>   getOutputItemId,
            Func<BuildingSaveData, float> getCollectorRate,
            Func<int, float>      getItemSellValue,
            Func<int, int, IdleRecipeInfo> getRecipeInfo,
            Func<int, float>      getSpringFlow,
            Func<int, float>      getFlowDraw,
            float effectiveItemsPerSecondMultiplier,
            string snapshotTimestamp,
            float surplusSaleFraction,
            Func<BuildingSaveData, float> getManagerOutputMultiplier = null)
        {
            var snapshot = new IdleCollectionSnapshot { snapshotTimestampUtc = snapshotTimestamp };
            if (grid == null) return snapshot;
            var buildings = grid.buildings ?? new List<BuildingSaveData>();
            var conveyors = grid.conveyors ?? new List<ConveyorSaveData>();
            float eff = effectiveItemsPerSecondMultiplier;

            // ---- Channel cells and their downstream links ----------------------------------------
            var next  = new Dictionary<Vector2Int, Vector2Int?>();
            var order = new List<Vector2Int>();
            foreach (var chain in conveyors)
            {
                if (chain?.cells == null || chain.cells.Length < 2) continue;
                int len = chain.cells.Length / 2;
                for (int i = 0; i < len; i++)
                {
                    var c = new Vector2Int(chain.cells[i * 2], chain.cells[i * 2 + 1]);
                    if (!next.ContainsKey(c)) { next[c] = null; order.Add(c); }
                    if (i + 1 < len) next[c] = new Vector2Int(chain.cells[i * 2 + 2], chain.cells[i * 2 + 3]);
                }
            }
            // A chain that joins another ends beside the merge cell: link it on in its travel direction.
            foreach (var chain in conveyors)
            {
                if (chain?.cells == null || chain.cells.Length < 4) continue;
                int len  = chain.cells.Length / 2;
                var last = new Vector2Int(chain.cells[len * 2 - 2], chain.cells[len * 2 - 1]);
                var prev = new Vector2Int(chain.cells[len * 2 - 4], chain.cells[len * 2 - 3]);
                var ahead = last + (last - prev);
                if (next[last] == null && next.ContainsKey(ahead) && ahead != prev) next[last] = ahead;
            }
            if (order.Count == 0) return snapshot;

            // Heads first: depth = hops to the mouth, larger first.
            var depth = new Dictionary<Vector2Int, int>();
            int Depth(Vector2Int c, int guard)
            {
                if (depth.TryGetValue(c, out int d)) return d;
                if (guard > order.Count) return 0;
                d = next[c] is Vector2Int n && next.ContainsKey(n) ? Depth(n, guard + 1) + 1 : 0;
                depth[c] = d;
                return d;
            }
            foreach (var c in order) Depth(c, 0);
            order.Sort((a, b) => depth[b].CompareTo(depth[a]));

            // ---- Buildings by cell -----------------------------------------------------------------
            var cellMap = new Dictionary<Vector2Int, BuildingSaveData>();
            foreach (var b in buildings)
            {
                if (b?.position == null || b.position.Length < 2) continue;
                var fp = getFootprint(b.buildingId);
                if (b.rotation % 2 == 1) fp = new Vector2Int(fp.y, fp.x);
                for (int dx = 0; dx < Mathf.Max(1, fp.x); dx++)
                    for (int dy = 0; dy < Mathf.Max(1, fp.y); dy++)
                        cellMap[new Vector2Int(b.position[0] + dx, b.position[1] + dy)] = b;
            }

            // ---- Walk the streams -----------------------------------------------------------------
            var carried = new Dictionary<Vector2Int, (float flow, bool source, Dictionary<int, float> items)>();
            var seen    = new HashSet<BuildingSaveData>();
            float Mgr(BuildingSaveData b) => getManagerOutputMultiplier?.Invoke(b) ?? 1f;

            foreach (var cell in order)
            {
                carried.TryGetValue(cell, out var here);
                float flow   = here.flow;
                bool  source = here.source;
                var   items  = here.items ?? new Dictionary<int, float>();

                var bank = new List<BuildingSaveData>(4);
                foreach (var d in Dirs)
                {
                    if (!cellMap.TryGetValue(cell + d, out var b)) continue;
                    if (next.ContainsKey(cell + d)) continue;
                    if (bank.Contains(b)) continue;
                    float spring = getSpringFlow(b.buildingId);
                    if (spring > 0f) { if (seen.Add(b)) { flow += spring; source = true; } continue; }
                    bank.Add(b);
                }

                foreach (var b in bank)
                {
                    if (isEntropySink(b.buildingId) || !seen.Add(b)) continue;
                    float share = source ? StreamRules.Take(ref flow, getFlowDraw(b.buildingId)) : 0f;
                    if (share <= 0f) continue;

                    if (isCollector(b.buildingId))
                    {
                        int item = getOutputItemId(b.buildingId, b.recipeId);
                        float rate = getCollectorRate(b) * eff * share * Mgr(b);
                        if (item >= 0 && rate > 0f) Add(items, item, rate);
                        continue;
                    }

                    var r = getRecipeInfo(b.buildingId, b.recipeId);
                    if (!r.Valid || r.OutputItemId < 0) continue;
                    float crafts = r.CraftTimeSeconds > 0f ? eff * share / r.CraftTimeSeconds : 0f;
                    int nIn = r.InputItemIds?.Length ?? 0;
                    for (int k = 0; k < nIn; k++)
                    {
                        int q = r.InputQuantities != null && k < r.InputQuantities.Length ? r.InputQuantities[k] : 1;
                        if (q <= 0) continue;
                        items.TryGetValue(r.InputItemIds[k], out float avail);
                        crafts = Mathf.Min(crafts, avail / q);
                    }
                    if (crafts <= 0f) continue;
                    for (int k = 0; k < nIn; k++)
                    {
                        int q = r.InputQuantities != null && k < r.InputQuantities.Length ? r.InputQuantities[k] : 1;
                        if (q > 0) Add(items, r.InputItemIds[k], -crafts * q);
                    }
                    int outQty = r.OutputQuantity > 0 ? r.OutputQuantity : 1;
                    Add(items, r.OutputItemId, crafts * outQty * Mgr(b));
                    if (r.ByproductEntropy > 0f)
                        snapshot.chains.Add(new IdleChainEntry
                        {
                            itemId = r.OutputItemId, itemsPerSecond = crafts, endsAtEntropySink = true,
                            baseSellValue = r.ByproductEntropy, notDelivered = true,
                        });
                }

                if (next[cell] is Vector2Int nx && next.ContainsKey(nx))
                {
                    carried.TryGetValue(nx, out var down);
                    var merged = down.items ?? new Dictionary<int, float>();
                    foreach (var kv in items) Add(merged, kv.Key, kv.Value);
                    carried[nx] = (down.flow + flow, down.source || source, merged);
                    continue;
                }

                // ---- Mouth --------------------------------------------------------------------------
                bool sink = false;
                foreach (var d in Dirs)
                    if (cellMap.TryGetValue(cell + d, out var b) && isEntropySink(b.buildingId)) { sink = true; break; }
                if (!source) continue;
                foreach (var kv in items)
                {
                    if (kv.Value <= 1e-6f) continue;
                    float price = getItemSellValue(kv.Key);
                    snapshot.chains.Add(new IdleChainEntry
                    {
                        itemId            = kv.Key,
                        itemsPerSecond    = kv.Value,
                        endsAtEntropySink = true,
                        baseSellValue     = sink ? price : price * surplusSaleFraction,
                        notDelivered      = true,   // never Maxwell's Demon: no world-task credit (WorldTasks)
                    });
                }
            }

            GameLogger.Debug($"[StreamIdle] cells={order.Count} buildings={buildings.Count} chains={snapshot.chains.Count}");
            return snapshot;
        }

        static void Add(Dictionary<int, float> items, int id, float rate)
        {
            items.TryGetValue(id, out float have);
            have += rate;
            if (have <= 1e-6f) items.Remove(id); else items[id] = have;
        }
    }
}
