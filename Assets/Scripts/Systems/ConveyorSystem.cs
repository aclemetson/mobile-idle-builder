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
    ///   2. Advance every item (<see cref="ConveyorItem"/> buffer, front first) up to the item ahead.
    ///   3. Transfer items: segment→next-segment or segment→building input buffer.
    ///   4. Pull items from building output buffers onto chain-head segments.
    ///
    /// A tile holds up to <see cref="BeltSettings.ItemsPerTile"/> items. Items keep
    /// <see cref="BeltSettings.Spacing"/> tiles apart along the whole chain (across tile edges too), so a
    /// belt carries speed / spacing items per second and a blocked belt packs at that spacing.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProductionSystem))]
    public partial class ConveyorSystem : SystemBase
    {
        private EntityQuery _segmentQuery;
        private EntityQuery _buildingQuery;
        private float       _debugTimer;
        // Fractional entropy from surplus sales not yet paid: a 1e quark at 50% is worth 0.5, so two of
        // them pay 1 instead of each truncating to 0.
        private double      _surplusCarry;

        protected override void OnCreate()
        {
            RequireForUpdate<ConveyorSegmentData>();

            _segmentQuery  = GetEntityQuery(typeof(ConveyorSegmentData));
            _buildingQuery = GetEntityQuery(typeof(BuildingData), typeof(GridPosition));
        }

        protected override void OnUpdate()
        {
            float dt = SystemAPI.Time.DeltaTime;
            // Research effects (Transportation I etc.): belts move faster by this factor. 1 = no bonus.
            float speedMult = SystemAPI.TryGetSingleton<ResearchEffects>(out var fx) ? fx.ConveyorSpeedMult : 1f;
            if (speedMult <= 0f) speedMult = 1f;
            int   itemsPerTile = SystemAPI.TryGetSingleton<BeltSettings>(out var bs)
                ? BeltSettings.Clamp(bs.ItemsPerTile) : BeltSettings.DefaultItemsPerTile;
            float spacing = BeltSettings.Spacing(itemsPerTile);
            _debugTimer += dt;
            bool debugLog = _debugTimer >= 2f;
            if (debugLog) _debugTimer = 0f;

            // ----------------------------------------------------------------
            // Pass 1 — Build cell→entity lookup maps
            // ----------------------------------------------------------------
            var segEntities  = _segmentQuery.ToEntityArray(Allocator.Temp);
            var bldgEntities = _buildingQuery.ToEntityArray(Allocator.Temp);

            // Every segment carries an item buffer (ConveyorPlacer adds it; this covers any made elsewhere).
            // Structural changes happen here, before any buffer handle is taken.
            for (int i = 0; i < segEntities.Length; i++)
                if (!EntityManager.HasBuffer<ConveyorItem>(segEntities[i]))
                    EntityManager.AddBuffer<ConveyorItem>(segEntities[i]);

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
            // Pass 2 — Advance items (back-pressure aware)
            //
            // The front item runs to the hand-off point (1) while the next segment has room, else it
            // stops one spacing behind that segment's rearmost item. A tail's front item runs to 1 when an
            // adjacent input takes it now, else it rests mid-slot. Every item behind keeps one spacing
            // to the item ahead, so queued items pack side by side.
            // ----------------------------------------------------------------
            // Tails first, so each segment sees where the segment ahead has moved to this frame. Reading a
            // stale rear costs a frame at every tile edge and the belt runs slow.
            var tailFirst = TailFirstOrder(segEntities);
            for (int i = 0; i < tailFirst.Count; i++)
            {
                var e   = tailFirst[i];
                var buf = EntityManager.GetBuffer<ConveyorItem>(e);
                if (buf.Length == 0) continue;

                var   seg  = EntityManager.GetComponentData<ConveyorSegmentData>(e);
                float step = dt * speedMult / seg.TransportTime;

                float frontLimit = seg.NextSegment != Entity.Null
                    ? BeltMotion.FrontLimitBehind(Rear(seg.NextSegment), spacing)
                    : TryPickInput(e, seg, buf[0].ItemID, buildingMap, out _, out _, out _)
                        ? BeltMotion.Open : BeltMotion.TailRest(spacing);

                for (int k = 0; k < buf.Length; k++)
                {
                    var item  = buf[k];
                    float lim = k == 0 ? frontLimit : buf[k - 1].Progress - spacing;
                    item.Progress = BeltMotion.Advance(item.Progress, step, lim);
                    buf[k] = item;
                }
            }

            // ----------------------------------------------------------------
            // Pass 3 — Transfer items
            //
            // Tail segments deposit into an adjacent building input (independent contention handled
            // by the input buffer). Mid-chain segments hand off to their single NextSegment; when
            // several segments feed one cell (a 2-3 input merge), only one item enters the merge
            // cell per frame and the inbound directions take turns via the cell's MergeCursor, so
            // no branch starves.
            // ----------------------------------------------------------------
            var buildingDeposits = new List<(Entity from, Entity bldg, int itemID, int deliverDir)>();
            var overflows         = new List<(Entity from, int itemID)>();
            var mergeTargets      = new HashSet<Entity>();

            for (int i = 0; i < segEntities.Length; i++)
            {
                var e = segEntities[i];
                if (!ConveyorItems.Has(EntityManager, e)) continue;

                var seg  = EntityManager.GetComponentData<ConveyorSegmentData>(e);
                var item = ConveyorItems.Front(EntityManager, e);
                if (item.Progress < 1f) continue;

                if (seg.NextSegment != Entity.Null)
                {
                    // Defer to the per-target round-robin below (handles single and merge cells alike).
                    if (BeltMotion.HasRoom(Rear(seg.NextSegment), item.Progress - 1f, spacing))
                        mergeTargets.Add(seg.NextSegment);
                    // else: back-pressure — item waits
                }
                else
                {
                    // Tail segment — deposit into a building input whose port faces this cell,
                    // in whichever adjacent direction it sits (independent of the belt's exit dir).
                    if (TryPickInput(e, seg, item.ItemID, buildingMap, out Entity bldg, out int deliverDir, out var decision))
                    {
                        switch (decision)
                        {
                            case InputDecision.Accept:   buildingDeposits.Add((e, bldg, item.ItemID, deliverDir)); break;
                            case InputDecision.Overflow: overflows.Add((e, item.ItemID));                          break;
                            // Wait: input full (for this item) or item unused -- it waits on the belt
                        }
                    }
                    // No adjacent input port: item waits at tail
                }
            }

            foreach (var (from, bldg, itemID, deliverDir) in buildingDeposits)
            {
                if (!FrontIsReady(from, itemID)) continue;
                SlotBufferUtils.AddToInputBuffer(EntityManager.GetBuffer<BuildingInputSlot>(bldg), itemID, 1);
                ConveyorItems.RemoveFront(EntityManager, from);

                // Input particle burst toward the building's entry face (the delivery direction,
                // which may differ from the belt's own exit direction).
                if (BuildingInputFX.Instance != null)
                {
                    var seg = EntityManager.GetComponentData<ConveyorSegmentData>(from);
                    BuildingInputFX.Instance.Trigger(seg.Cell, deliverDir, itemID);
                }
            }

            // Jam overflow (#200): the front item was blocking an item the building needs, so it is
            // sold at the surplus price (#312) and the belt keeps flowing. It used to go to the
            // inventory, where nothing ever sold it.
            if (overflows.Count > 0)
            {
                float fraction = SurplusSale.CurrentFraction();
                foreach (var (from, itemID) in overflows)
                {
                    if (!FrontIsReady(from, itemID)) continue;
                    _surplusCarry += SurplusSale.ExactValue(SurplusSale.DemonPrice(itemID), 1, fraction);
                    ConveyorItems.RemoveFront(EntityManager, from);
                }
                long earned = (long)System.Math.Floor(_surplusCarry);
                if (earned > 0 && SystemAPI.TryGetSingletonEntity<PlayerProgressData>(out Entity progressEntity))
                {
                    _surplusCarry -= earned;
                    var progress = EntityManager.GetComponentData<PlayerProgressData>(progressEntity);
                    progress.BaseCurrency += earned;
                    EntityManager.SetComponentData(progressEntity, progress);
                    if (SystemAPI.TryGetSingletonEntity<AutomationIncome>(out Entity incomeEnt))
                    {
                        var income = EntityManager.GetComponentData<AutomationIncome>(incomeEnt);
                        income.Total += earned;
                        EntityManager.SetComponentData(incomeEnt, income);
                    }
                }
                if (debugLog) GameLogger.Develop($"[Conveyor] Jam overflow: {overflows.Count} item(s) sold, paid {earned}");
            }

            foreach (var target in mergeTargets)
            {
                float? targetRear = Rear(target);
                var tSeg = EntityManager.GetComponentData<ConveyorSegmentData>(target);

                // Round-robin over the 4 directions starting at MergeCursor; accept the first inbound
                // (a neighbour whose ExitDir points back at us) that has a ready item.
                int cursor = ((tSeg.MergeCursor % 4) + 4) % 4;
                Entity chosen = Entity.Null;
                int    chosenDir = -1;
                for (int k = 0; k < 4; k++)
                {
                    int dir   = (cursor + k) % 4;
                    int2 nc   = AdjacentCell(tSeg.Cell, dir);
                    if (!segMap.TryGetValue(nc, out Entity inSeg)) continue;

                    var inData = EntityManager.GetComponentData<ConveyorSegmentData>(inSeg);
                    if (inData.OutputBlocked) continue;                                         // dead-end: not connected
                    int2 inExitCell = AdjacentCell(inData.Cell, inData.ExitDir);
                    if (inExitCell.x != tSeg.Cell.x || inExitCell.y != tSeg.Cell.y) continue; // not an inbound
                    if (!ConveyorItems.Has(EntityManager, inSeg)) continue;
                    float frontProgress = ConveyorItems.Front(EntityManager, inSeg).Progress;
                    if (frontProgress < 1f) continue;
                    if (!BeltMotion.HasRoom(targetRear, frontProgress - 1f, spacing)) continue;

                    chosen    = inSeg;
                    chosenDir = dir;
                    break;
                }

                if (chosen == Entity.Null) continue;

                var chosenSeg  = EntityManager.GetComponentData<ConveyorSegmentData>(chosen);
                var chosenItem = ConveyorItems.Front(EntityManager, chosen);

                // Joins at the rear, keeping the overshoot so frame timing doesn't cost throughput.
                EntityManager.GetBuffer<ConveyorItem>(target).Add(new ConveyorItem
                {
                    ItemID   = chosenItem.ItemID,
                    Progress = Mathf.Max(0f, chosenItem.Progress - 1f)
                });
                ConveyorItems.RemoveFront(EntityManager, chosen);

                // Animate the item from the edge it actually arrived through, and advance the cursor.
                tSeg.EntryDir    = chosenSeg.ExitDir;
                tSeg.MergeCursor = (chosenDir + 1) % 4;
                EntityManager.SetComponentData(target, tSeg);
            }

            // ----------------------------------------------------------------
            // Pass 4 — Pull items from building output buffers onto chain heads
            // ----------------------------------------------------------------
            var pulls = new List<(Entity head, int itemID)>();
            var splitterHeads = new Dictionary<Entity, List<(Entity head, int facing)>>();

            for (int i = 0; i < segEntities.Length; i++)
            {
                var e   = segEntities[i];
                var seg = EntityManager.GetComponentData<ConveyorSegmentData>(e);
                if (!seg.IsChainHead) continue;
                if (!BeltMotion.HasRoom(Rear(e), 0f, spacing)) continue; // no room at the entry edge yet

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

                if (EntityManager.HasComponent<SplitterTag>(bldg))
                {
                    // A Belt Splitter: its sides take turns, decided once every ready head is known.
                    if (!splitterHeads.TryGetValue(bldg, out var heads))
                        splitterHeads[bldg] = heads = new List<(Entity head, int facing)>();
                    heads.Add((e, portFacing));
                    continue;
                }

                int itemID = outputBuf[0].ItemID;
                if (EntityManager.HasBuffer<OutputPortFilter>(bldg))
                {
                    // A Filter (#321): this side only takes the items its rules send here.
                    var rules = FilterSystem.ReadRules(EntityManager.GetBuffer<OutputPortFilter>(bldg, isReadOnly: true));
                    itemID = -1;
                    for (int k = 0; k < outputBuf.Length && itemID < 0; k++)
                        if (outputBuf[k].Quantity > 0 && PortFilterRules.Route(rules, outputBuf[k].ItemID) == portFacing)
                            itemID = outputBuf[k].ItemID;
                    if (itemID < 0) continue;
                }
                SlotBufferUtils.RemoveFromOutputBuffer(outputBuf, itemID, 1);
                pulls.Add((e, itemID));
                if (debugLog)
                    GameLogger.Develop($"[ConveyorSystem] Chain head @ {seg.Cell}: pulled itemID={itemID} " +
                              $"from building at {sourceCell} (portFacing={portFacing}) onto belt.");
            }

            foreach (var kv in splitterHeads)
                PullFromSplitter(kv.Key, kv.Value, pulls);

            foreach (var (head, itemID) in pulls)
            {
                // Enter exactly one spacing behind the rearmost item (or at the edge of an empty head), so
                // a full-speed source fills the belt at its rated spacing whatever the frame timing.
                float? rear = Rear(head);
                if (!BeltMotion.HasRoom(rear, 0f, spacing)) continue;
                EntityManager.GetBuffer<ConveyorItem>(head).Add(new ConveyorItem
                {
                    ItemID   = itemID,
                    Progress = rear == null ? 0f : rear.Value - spacing
                });
            }

            segEntities.Dispose();
            bldgEntities.Dispose();
            segMap.Dispose();
            buildingMap.Dispose();
        }

        /// <summary>
        /// Belt Splitter: gives its next item to the side whose turn it is, or the next ready side after it
        /// (<see cref="SplitterRotation"/>), then passes the turn on. One item per splitter per tick, far more
        /// than a belt carries, so the branches share the flow evenly whatever order the heads were visited in.
        /// </summary>
        private void PullFromSplitter(Entity bldg, List<(Entity head, int facing)> readyHeads,
                                      List<(Entity head, int itemID)> pulls)
        {
            var outputBuf = EntityManager.GetBuffer<BuildingOutputSlot>(bldg);
            if (outputBuf.Length == 0) return;

            var order = new List<int>(3);
            var ports = EntityManager.GetBuffer<PlacedPortData>(bldg, isReadOnly: true);
            for (int p = 0; p < ports.Length; p++)
                if (ports[p].PortType == (int)PortType.Output && !order.Contains(ports[p].Facing))
                    order.Add(ports[p].Facing);

            var ready = new HashSet<int>();
            foreach (var (_, facing) in readyHeads) ready.Add(facing);

            var state  = EntityManager.GetComponentData<SplitterState>(bldg);
            int chosen = SplitterRotation.Pick(order, state.NextFacing, ready);
            if (chosen < 0) return;

            foreach (var (head, facing) in readyHeads)
            {
                if (facing != chosen) continue;
                int itemID = outputBuf[0].ItemID;
                SlotBufferUtils.RemoveFromOutputBuffer(outputBuf, itemID, 1);
                pulls.Add((head, itemID));
                break;
            }

            state.NextFacing = SplitterRotation.After(order, chosen);
            EntityManager.SetComponentData(bldg, state);
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
        // How far upstream (in segments) a jammed tail looks for an item the building is short of.
        private const int JamLookback = 32;

        /// <summary>
        /// What the input of <paramref name="bldg"/> does with <paramref name="itemID"/> waiting on
        /// <paramref name="tail"/>. See <see cref="InputAcceptance"/> for the rules (#200).
        /// </summary>
        private InputDecision DecideDeposit(Entity tail, Entity bldg, int itemID)
        {
            var inputBuf = EntityManager.GetBuffer<BuildingInputSlot>(bldg, isReadOnly: true);
            int cap      = EntityManager.GetComponentData<BuildingInventoryConfig>(bldg).InputCapacity;
            int count    = SlotBufferUtils.CountInInputBuffer(inputBuf, itemID);

            bool recipeSet = EntityManager.HasComponent<RecipeProcessData>(bldg)
                          && EntityManager.HasBuffer<RecipeInputSlot>(bldg)
                          && EntityManager.GetComponentData<RecipeProcessData>(bldg).RecipeID >= 0;
            if (!recipeSet)
                return InputAcceptance.Decide(count, cap, recipeSet: false, recipeUsesItem: false, neededItemBehind: false);

            var  recipeInputs = EntityManager.GetBuffer<RecipeInputSlot>(bldg, isReadOnly: true);
            bool uses         = InputAcceptance.RecipeUses(recipeInputs, itemID);
            if (uses && count < cap) return InputDecision.Accept; // common case: skip the belt walk

            return InputAcceptance.Decide(count, cap, recipeSet: true, uses,
                NeededItemBehind(tail, recipeInputs, inputBuf));
        }

        /// <summary>
        /// Walks the belt upstream from <paramref name="tail"/> (primary inbound chain, up to
        /// <see cref="JamLookback"/> segments) looking for an item the building is short of.
        /// </summary>
        private bool NeededItemBehind(Entity tail, DynamicBuffer<RecipeInputSlot> recipeInputs,
                                      DynamicBuffer<BuildingInputSlot> inputBuf)
        {
            // Items queued behind the front on the tail itself come first.
            if (EntityManager.HasBuffer<ConveyorItem>(tail))
            {
                var own = EntityManager.GetBuffer<ConveyorItem>(tail, isReadOnly: true);
                for (int k = 1; k < own.Length; k++)
                    if (InputAcceptance.IsShortOf(recipeInputs, inputBuf, own[k].ItemID)) return true;
            }
            Entity cur = EntityManager.GetComponentData<ConveyorSegmentData>(tail).PrevSegment;
            for (int n = 0; n < JamLookback && cur != Entity.Null && EntityManager.Exists(cur); n++)
            {
                if (EntityManager.HasBuffer<ConveyorItem>(cur))
                {
                    var items = EntityManager.GetBuffer<ConveyorItem>(cur, isReadOnly: true);
                    for (int k = 0; k < items.Length; k++)
                        if (InputAcceptance.IsShortOf(recipeInputs, inputBuf, items[k].ItemID)) return true;
                }
                cur = EntityManager.GetComponentData<ConveyorSegmentData>(cur).PrevSegment;
            }
            return false;
        }

        /// <summary>Segments ordered by how many hops they are from their chain's tail, tails first.</summary>
        private List<Entity> TailFirstOrder(NativeArray<Entity> segs)
        {
            var depth = new Dictionary<Entity, int>(segs.Length);
            var path  = new List<Entity>();
            for (int i = 0; i < segs.Length; i++)
            {
                path.Clear();
                Entity cur = segs[i];
                int known = -1;
                // Walk forward until a segment with a known depth, the tail, or a loop (guarded).
                while (cur != Entity.Null && path.Count <= segs.Length)
                {
                    if (depth.TryGetValue(cur, out known)) break;
                    path.Add(cur);
                    if (!EntityManager.Exists(cur)) { cur = Entity.Null; break; }
                    cur = EntityManager.GetComponentData<ConveyorSegmentData>(cur).NextSegment;
                    known = -1;
                }
                int d = known; // depth of the first known segment past the path, -1 when the path ended at a tail
                for (int k = path.Count - 1; k >= 0; k--)
                {
                    d++;
                    if (!depth.ContainsKey(path[k])) depth[path[k]] = d;
                }
            }
            var order = new List<Entity>(segs.Length);
            for (int i = 0; i < segs.Length; i++) order.Add(segs[i]);
            order.Sort((a, b) => depth[a].CompareTo(depth[b]));
            return order;
        }

        /// <summary>Progress of the rearmost item on <paramref name="seg"/>, or null when it carries none.</summary>
        private float? Rear(Entity seg)
        {
            if (!EntityManager.HasBuffer<ConveyorItem>(seg)) return null;
            var buf = EntityManager.GetBuffer<ConveyorItem>(seg, isReadOnly: true);
            return buf.Length == 0 ? null : buf[buf.Length - 1].Progress;
        }

        /// <summary>True when <paramref name="seg"/>'s front item is still <paramref name="itemID"/> and ready to leave.</summary>
        private bool FrontIsReady(Entity seg, int itemID)
        {
            if (!ConveyorItems.Has(EntityManager, seg)) return false;
            var front = ConveyorItems.Front(EntityManager, seg);
            return front.ItemID == itemID && front.Progress >= 1f;
        }

        /// <summary>
        /// The input a tail item goes to, and what that input does with it. A tail can sit in front of
        /// several inputs, and Maxwell's Demon has one on every edge tile, so a belt ending beside it
        /// used to sell whatever it carried to the first input the scan met (#275: every proton bound
        /// for a 2x1 Atom Generator next to the Demon was sold). Inputs are tried in
        /// <see cref="TailInputPriority"/> order and the first that doesn't make the item wait wins, so
        /// a building that needs the item gets it and the Demon still takes anything nobody else wants.
        /// False (item waits) when no adjacent input takes it now.
        /// </summary>
        private bool TryPickInput(Entity tail, ConveyorSegmentData seg, int itemID,
                                  NativeHashMap<int2, Entity> buildingMap,
                                  out Entity building, out int deliverDir, out InputDecision decision)
        {
            building   = Entity.Null;
            deliverDir = -1;
            decision   = InputDecision.Wait;
            int bestRank = int.MaxValue;
            for (int dir = 0; dir < 4; dir++)
            {
                int2 candidate = AdjacentCell(seg.Cell, dir);
                if (!buildingMap.TryGetValue(candidate, out Entity cand)) continue;
                if (!HasMatchingPort(cand, candidate, dir, PortType.Input)) continue;

                int rank = TailInputPriority.Rank(dir == seg.ExitDir, EntityManager.HasComponent<EntropySinkTag>(cand));
                if (rank >= bestRank) continue;
                var d = DecideDeposit(tail, cand, itemID);
                if (d == InputDecision.Wait) continue;

                bestRank   = rank;
                building   = cand;
                deliverDir = dir;
                decision   = d;
            }
            return building != Entity.Null;
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
