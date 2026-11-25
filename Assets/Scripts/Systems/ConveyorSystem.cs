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
            // Speed burst: the whole factory runs on a faster clock (FactoryClock, 1 when absent).
            float clockRate = FactoryClock.Sanitize(SystemAPI.TryGetSingleton<FactoryClock>(out var fc) ? fc.Rate : 1f);
            float dt = SystemAPI.Time.DeltaTime * clockRate;
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

            // Overpasses: each ramp-down, with its ramp-up and the side the span arrives on (not a neighbour).
            var rampDowns = new Dictionary<int2, (Entity up, int side)>();
            for (int i = 0; i < segEntities.Length; i++)
            {
                var seg = EntityManager.GetComponentData<ConveyorSegmentData>(segEntities[i]);
                segMap.TryAdd(seg.Cell, segEntities[i]);
                if (EntityManager.HasComponent<BeltBridge>(segEntities[i]))
                {
                    var b = EntityManager.GetComponentData<BeltBridge>(segEntities[i]);
                    rampDowns[b.Exit] = (segEntities[i], ConveyorTopology.DirBetween(b.Exit, seg.Cell));
                }
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

            // Belt splits: every item that reached one is given its exit by weighted turns, among the sides that
            // have a belt, so it curves toward its branch as it crosses the tile.
            PlanSplitExits(segEntities, segMap);

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
                float len  = Len(e);                                   // tiles (an overpass span is several)
                float step = dt * speedMult / (seg.TransportTime * len);
                float sp   = spacing / len;                            // item spacing in this segment's progress

                // Streams (Chemistry): a channel no Spring feeds is dry and nothing on it moves; a stream's
                // mouth never holds items back (a Calorimeter takes them, else they are sold as surplus).
                bool isStream = EntityManager.HasComponent<StreamSegment>(e);
                if (isStream && EntityManager.GetComponentData<StreamSegment>(e).HasSource == 0) continue;

                float frontLimit = EntityManager.HasComponent<BeltSplit>(e) && SplitHasOutput(e, seg, segMap)
                    ? SplitFrontLimit(e, seg, segMap, spacing)
                    : seg.NextSegment != Entity.Null
                    ? FrontLimitInto(e, seg.NextSegment, spacing)
                    : isStream || TryPickInput(e, seg, buf[0].ItemID, buildingMap, out _, out _, out _)
                        ? BeltMotion.Open : BeltMotion.TailRest(sp);

                for (int k = 0; k < buf.Length; k++)
                {
                    var item  = buf[k];
                    float lim = k == 0 ? frontLimit : buf[k - 1].Progress - sp;
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
            var overflows         = new List<(Entity from, int itemID, Entity bldg)>();
            var mergeTargets      = new HashSet<Entity>();
            var splitChoice       = new Dictionary<Entity, int>();   // split segment -> the side it hands off to now

            for (int i = 0; i < segEntities.Length; i++)
            {
                var e = segEntities[i];
                if (!ConveyorItems.Has(EntityManager, e)) continue;

                var seg  = EntityManager.GetComponentData<ConveyorSegmentData>(e);
                var item = ConveyorItems.Front(EntityManager, e);
                if (item.Progress < 1f) continue;

                if (EntityManager.HasComponent<BeltSplit>(e) && SplitHasOutput(e, seg, segMap))
                {
                    // A split: its planned side if that belt has room, else the next side by turns that does.
                    int dir = ChooseSplitExit(e, seg, segMap, item, spacing);
                    if (dir >= 0)
                    {
                        splitChoice[e] = dir;
                        mergeTargets.Add(segMap[AdjacentCell(seg.Cell, dir)]);
                    }
                    // else every output is backed up: the item waits
                }
                else if (seg.NextSegment != Entity.Null)
                {
                    // Defer to the per-target round-robin below (handles single and merge cells alike).
                    if (RoomInto(seg.NextSegment, e, item.Progress, spacing))
                        mergeTargets.Add(seg.NextSegment);
                    // else: back-pressure — item waits
                }
                else
                {
                    // Tail segment — deposit into a building input whose port faces this cell,
                    // in whichever adjacent direction it sits (independent of the belt's exit dir).
                    bool streamMouth = EntityManager.HasComponent<StreamSegment>(e);
                    if (TryPickInput(e, seg, item.ItemID, buildingMap, out Entity bldg, out int deliverDir, out var decision))
                    {
                        switch (decision)
                        {
                            case InputDecision.Accept:   buildingDeposits.Add((e, bldg, item.ItemID, deliverDir)); break;
                            case InputDecision.Overflow: overflows.Add((e, item.ItemID, bldg));                    break;
                            // Wait: input full (for this item) or item unused -- it waits on the belt,
                            // except at a stream's mouth, which sells it as surplus.
                            default: if (streamMouth) overflows.Add((e, item.ItemID, Entity.Null));                 break;
                        }
                    }
                    // No adjacent input port: a belt's item waits at the tail; a stream's open mouth sells it.
                    else if (streamMouth) overflows.Add((e, item.ItemID, Entity.Null));
                }
            }

            foreach (var (from, bldg, itemID, deliverDir) in buildingDeposits)
            {
                if (!FrontIsReady(from, itemID)) continue;
                SlotBufferUtils.AddToInputBuffer(EntityManager.GetBuffer<BuildingInputSlot>(bldg), itemID, 1);
                ConveyorItems.RemoveFront(EntityManager, from);

                // The delivery direction may differ from the belt's own exit direction: log it so the
                // tail is drawn bending into the input it really feeds, and aim the input particle burst.
                var seg = EntityManager.GetComponentData<ConveyorSegmentData>(from);
                TailDeliveryLog.Record(seg.Cell, deliverDir);
                if (BuildingInputFX.Instance != null)
                    BuildingInputFX.Instance.Trigger(seg.Cell, deliverDir, itemID);
            }

            // Surplus sales: the item reached a full input the recipe uses (full-input autosell), or it was
            // jamming an item the building needs (#200). Either way it is sold at the surplus price (#312) and
            // the belt keeps flowing. Each sale is logged per building for the inspector's surplus row.
            if (overflows.Count > 0)
            {
                float fraction = SurplusSale.CurrentFraction();
                // Cyclotron Kick boost (1 when none is running) multiplies what the sales pay.
                double boost = SystemAPI.TryGetSingleton<EntropyIncomeBoost>(out var eb) && eb.Mult > 0f ? eb.Mult : 1d;
                float now      = UnityEngine.Time.time;
                foreach (var (from, itemID, bldg) in overflows)
                {
                    if (!FrontIsReady(from, itemID)) continue;
                    double value = SurplusSale.ExactValue(SurplusSale.DemonPrice(itemID), 1, fraction) * boost;
                    _surplusCarry += value;
                    if (bldg != Entity.Null) SurplusSaleLog.Record(bldg, value, now);
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
                        income.Total     += earned;
                        income.Unboosted += earned / (boost * clockRate);
                        EntityManager.SetComponentData(incomeEnt, income);
                    }
                }
                if (debugLog) GameLogger.Develop($"[Conveyor] Surplus: {overflows.Count} item(s) sold, paid {earned}");
            }

            // Streams: buildings on the banks take the items their recipe uses as they pass.
            StreamSidePickup(segEntities, buildingMap);

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
                    Entity inSeg;
                    if (rampDowns.TryGetValue(tSeg.Cell, out var rd) && rd.side == dir)
                        inSeg = rd.up;                                    // a ramp-down takes its overpass on the span side
                    else if (!segMap.TryGetValue(nc, out inSeg)) continue;

                    var inData = EntityManager.GetComponentData<ConveyorSegmentData>(inSeg);
                    if (splitChoice.TryGetValue(inSeg, out int splitDir))
                    {
                        // A split hands off only to the side it chose this frame.
                        int2 c = AdjacentCell(inData.Cell, splitDir);
                        if (c.x != tSeg.Cell.x || c.y != tSeg.Cell.y) continue;
                    }
                    else
                    {
                        if (EntityManager.HasComponent<BeltSplit>(inSeg)) continue;                // a split waiting on another side
                        if (inData.OutputBlocked) continue;                                         // dead-end: not connected
                        // Linked to us (ConveyorTopology): its output points here, or it's our overpass. A belt
                        // running under a ramp points at it but isn't linked.
                        if (inData.NextSegment != target) continue;
                    }
                    if (!ConveyorItems.Has(EntityManager, inSeg)) continue;
                    float frontProgress = ConveyorItems.Front(EntityManager, inSeg).Progress;
                    if (frontProgress < 1f) continue;
                    if (!RoomInto(target, inSeg, frontProgress, spacing)) continue;

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
                    Progress = CarryInto(target, chosen, chosenItem.Progress),
                    InEdge   = ConveyorItem.EncodeEdge(chosenDir)   // the edge it came through (merge arm)
                });
                ConveyorItems.RemoveFront(EntityManager, chosen);

                // Animate the item from the edge it actually arrived through, and advance the cursor.
                tSeg.EntryDir    = OppositeDir(chosenDir);   // travel direction in (a split's side, or the inbound's ExitDir)
                tSeg.MergeCursor = (chosenDir + 1) % 4;
                EntityManager.SetComponentData(target, tSeg);
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
                if (!BeltMotion.HasRoom(Rear(e), 0f, spacing / Len(e))) continue; // no room at the entry edge yet

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

            foreach (var (head, itemID) in pulls)
            {
                // Enter exactly one spacing behind the rearmost item (or at the edge of an empty head), so
                // a full-speed source fills the belt at its rated spacing whatever the frame timing.
                float? rear = Rear(head);
                float  sp   = spacing / Len(head);
                if (!BeltMotion.HasRoom(rear, 0f, sp)) continue;
                EntityManager.GetBuffer<ConveyorItem>(head).Add(new ConveyorItem
                {
                    ItemID   = itemID,
                    Progress = rear == null ? 0f : rear.Value - sp
                });
            }

            // Streams: bank buildings drop their products into the middle of the tile beside them.
            StreamSideDrop(segEntities, buildingMap, spacing);

            segEntities.Dispose();
            bldgEntities.Dispose();
            segMap.Dispose();
            buildingMap.Dispose();
        }

        // ----------------------------------------------------------------
        // Belt splits (BeltSplit): weighted turns between 2-3 output sides
        // ----------------------------------------------------------------

        // True when at least one of the split's output sides has a belt to hand to.
        private bool SplitHasOutput(Entity e, ConveyorSegmentData seg, NativeHashMap<int2, Entity> segMap)
        {
            var split = EntityManager.GetComponentData<BeltSplit>(e);
            for (int d = 0; d < 4; d++)
                if (BeltSplitRules.Has(split.Mask, d) && segMap.ContainsKey(AdjacentCell(seg.Cell, d))) return true;
            return false;
        }

        // Gives every item on a split without a planned exit one, by weighted turns among sides that have a belt.
        private void PlanSplitExits(NativeArray<Entity> segs, NativeHashMap<int2, Entity> segMap)
        {
            for (int i = 0; i < segs.Length; i++)
            {
                var e = segs[i];
                if (!EntityManager.HasComponent<BeltSplit>(e) || !ConveyorItems.Has(EntityManager, e)) continue;
                var seg   = EntityManager.GetComponentData<ConveyorSegmentData>(e);
                var split = EntityManager.GetComponentData<BeltSplit>(e);
                var buf   = EntityManager.GetBuffer<ConveyorItem>(e);
                bool changed = false;
                for (int k = 0; k < buf.Length; k++)
                {
                    var it = buf[k];
                    if (it.OutEdge != 0 && BeltSplitRules.Has(split.Mask, it.OutEdge - 1)) continue;
                    int dir = BeltSplitRules.Pick(ref split, d => segMap.ContainsKey(AdjacentCell(seg.Cell, d)));
                    if (dir < 0) break;
                    it.OutEdge = ConveyorItem.EncodeEdge(dir);
                    buf[k]  = it;
                    changed = true;
                }
                if (changed) EntityManager.SetComponentData(e, split);
            }
        }

        // The front item may run to the edge when any output has room for it; else it stops behind the best one.
        private float SplitFrontLimit(Entity e, ConveyorSegmentData seg, NativeHashMap<int2, Entity> segMap, float spacing)
        {
            var split = EntityManager.GetComponentData<BeltSplit>(e);
            float best = float.NegativeInfinity;
            for (int d = 0; d < 4; d++)
            {
                if (!BeltSplitRules.Has(split.Mask, d) || !segMap.TryGetValue(AdjacentCell(seg.Cell, d), out Entity t)) continue;
                float lim = FrontLimitInto(e, t, spacing);
                if (lim > best) best = lim;
            }
            return best;
        }

        // The side a split's ready front item leaves by: its planned side when that belt has room, else the next
        // side by turns whose belt has room (the item's plan is rewritten). -1 when every output is backed up.
        private int ChooseSplitExit(Entity e, ConveyorSegmentData seg, NativeHashMap<int2, Entity> segMap,
                                    ConveyorItem front, float spacing)
        {
            bool Room(int d) =>
                segMap.TryGetValue(AdjacentCell(seg.Cell, d), out Entity t)
                && RoomInto(t, e, front.Progress, spacing);

            var split = EntityManager.GetComponentData<BeltSplit>(e);
            int planned = front.OutEdge - 1;
            if (planned >= 0 && BeltSplitRules.Has(split.Mask, planned) && Room(planned)) return planned;

            int dir = BeltSplitRules.Pick(ref split, d => Room(d));
            if (dir < 0) return -1;
            EntityManager.SetComponentData(e, split);
            var buf = EntityManager.GetBuffer<ConveyorItem>(e);
            var it  = buf[0];
            it.OutEdge = ConveyorItem.EncodeEdge(dir);
            buf[0] = it;
            return dir;
        }

        // ----------------------------------------------------------------
        // Streams (Chemistry, StreamSegment): side pickup and side drop
        // ----------------------------------------------------------------

        /// <summary>
        /// A building with an input port facing a stream segment takes, each frame, the first item on that
        /// segment its set recipe uses while that input is below its cap. Everything else floats past: no
        /// waiting, no surplus sale. Sinks are fed at the mouth only (tail path), never from the bank.
        /// </summary>
        private void StreamSidePickup(NativeArray<Entity> segs, NativeHashMap<int2, Entity> buildingMap)
        {
            for (int i = 0; i < segs.Length; i++)
            {
                var e = segs[i];
                if (!EntityManager.HasComponent<StreamSegment>(e) || !ConveyorItems.Has(EntityManager, e)) continue;
                var seg = EntityManager.GetComponentData<ConveyorSegmentData>(e);

                for (int dir = 0; dir < 4; dir++)
                {
                    int2 cell = AdjacentCell(seg.Cell, dir);
                    if (!buildingMap.TryGetValue(cell, out Entity bldg)) continue;
                    if (EntityManager.HasComponent<EntropySinkTag>(bldg)) continue;
                    if (!HasMatchingPort(bldg, cell, dir, PortType.Input)) continue;
                    if (!EntityManager.HasComponent<RecipeProcessData>(bldg) || !EntityManager.HasBuffer<RecipeInputSlot>(bldg)) continue;
                    if (EntityManager.GetComponentData<RecipeProcessData>(bldg).RecipeID < 0) continue;
                    if (!EntityManager.HasBuffer<BuildingInputSlot>(bldg) || !EntityManager.HasComponent<BuildingInventoryConfig>(bldg)) continue;

                    int cap   = EntityManager.GetComponentData<BuildingInventoryConfig>(bldg).InputCapacity;
                    var items = EntityManager.GetBuffer<ConveyorItem>(e);
                    var recipeInputs = EntityManager.GetBuffer<RecipeInputSlot>(bldg, isReadOnly: true);
                    var inputBuf     = EntityManager.GetBuffer<BuildingInputSlot>(bldg);
                    for (int k = 0; k < items.Length; k++)
                    {
                        int id = items[k].ItemID;
                        if (!InputAcceptance.RecipeUses(recipeInputs, id)) continue;
                        if (SlotBufferUtils.CountInInputBuffer(inputBuf, id) >= cap) continue;
                        SlotBufferUtils.AddToInputBuffer(inputBuf, id, 1);
                        items.RemoveAt(k);
                        if (BuildingInputFX.Instance != null) BuildingInputFX.Instance.Trigger(seg.Cell, dir, id);
                        break;
                    }
                    if (items.Length == 0) break;
                }
            }
        }

        /// <summary>
        /// A building with an output port facing a stream segment drops its next product into the middle of
        /// that tile when there is a gap (<see cref="StreamRules.DropIndex"/>); otherwise it keeps it. Chain
        /// heads are skipped: the head pull (pass 4) already takes from a building at the stream's start.
        /// </summary>
        private void StreamSideDrop(NativeArray<Entity> segs, NativeHashMap<int2, Entity> buildingMap, float spacing)
        {
            var progresses = new List<float>(4);
            for (int i = 0; i < segs.Length; i++)
            {
                var e = segs[i];
                if (!EntityManager.HasComponent<StreamSegment>(e)) continue;
                var seg = EntityManager.GetComponentData<ConveyorSegmentData>(e);
                if (seg.IsChainHead) continue;

                for (int dir = 0; dir < 4; dir++)
                {
                    int2 cell = AdjacentCell(seg.Cell, dir);
                    if (!buildingMap.TryGetValue(cell, out Entity bldg)) continue;
                    if (EntityManager.HasBuffer<OutputPortFilter>(bldg)) continue;
                    if (!HasMatchingPort(bldg, cell, OppositeDir(dir), PortType.Output)) continue;
                    if (!EntityManager.HasBuffer<BuildingOutputSlot>(bldg)) continue;
                    var outputBuf = EntityManager.GetBuffer<BuildingOutputSlot>(bldg);
                    if (outputBuf.Length == 0 || outputBuf[0].Quantity <= 0) continue;

                    var items = ConveyorItems.Get(EntityManager, e);
                    progresses.Clear();
                    for (int k = 0; k < items.Length; k++) progresses.Add(items[k].Progress);
                    int at = StreamRules.DropIndex(progresses, StreamRules.DropAt, spacing);
                    if (at < 0) continue;

                    int itemID = outputBuf[0].ItemID;
                    SlotBufferUtils.RemoveFromOutputBuffer(outputBuf, itemID, 1);
                    items.Insert(at, new ConveyorItem
                    {
                        ItemID   = itemID,
                        Progress = StreamRules.DropAt,
                        InEdge   = ConveyorItem.EncodeEdge(dir),
                    });
                }
            }
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
            if (uses)   // a craft bigger than the cap is gathered in the creation buffer (CraftStaging, #268)
                return count < cap ? InputDecision.Accept : InputDecision.Overflow; // skip the belt walk

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

        // ----------------------------------------------------------------
        // Overpasses (BeltBridge): a span's one segment is several tiles long. Progress runs 0..1 over all of it,
        // so its items step and sit closer in progress; these convert at a hand-off between segments of different
        // lengths, keeping belt speed and spacing in tiles everywhere.
        // ----------------------------------------------------------------

        /// <summary>Length of a segment in tiles: 1, or an overpass span's <see cref="BeltBridge.Length"/>.</summary>
        private float Len(Entity e) =>
            EntityManager.HasComponent<BeltBridge>(e) ? math.max(1, EntityManager.GetComponentData<BeltBridge>(e).Length) : 1f;

        /// <summary>How far <paramref name="cur"/>'s front item may go: one spacing (in tiles) behind <paramref name="next"/>'s rear.</summary>
        private float FrontLimitInto(Entity cur, Entity next, float spacing)
        {
            float? rear = Rear(next);
            if (rear == null) return BeltMotion.Open;
            float ln = Len(next);
            return 1f + (rear.Value - spacing / ln) * ln / Len(cur);
        }

        /// <summary>True when <paramref name="next"/> has room for <paramref name="from"/>'s front item (at <paramref name="frontProgress"/>).</summary>
        private bool RoomInto(Entity next, Entity from, float frontProgress, float spacing)
        {
            float ln = Len(next);
            return BeltMotion.HasRoom(Rear(next), (frontProgress - 1f) * Len(from) / ln, spacing / ln);
        }

        /// <summary>The handed-off item's progress on <paramref name="next"/>, keeping its overshoot in tiles.</summary>
        private float CarryInto(Entity next, Entity from, float progress) =>
            Mathf.Max(0f, (progress - 1f) * Len(from) / Len(next));

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
            // An input that takes the item always beats one that would sell it as surplus: never sell into a
            // full building while another input beside the tail (including the Demon, at full price) has room.
            Entity overflowBldg = Entity.Null;
            int    overflowDir  = -1;
            int bestAcceptRank = int.MaxValue, bestOverflowRank = int.MaxValue;
            // A stream's mouth feeds sinks only: bank buildings take what they need from the side.
            bool streamTail = EntityManager.HasComponent<StreamSegment>(tail);
            for (int dir = 0; dir < 4; dir++)
            {
                int2 candidate = AdjacentCell(seg.Cell, dir);
                if (!buildingMap.TryGetValue(candidate, out Entity cand)) continue;
                if (!HasMatchingPort(cand, candidate, dir, PortType.Input)) continue;
                if (streamTail && !EntityManager.HasComponent<EntropySinkTag>(cand)) continue;

                int rank = TailInputPriority.Rank(dir == seg.ExitDir, EntityManager.HasComponent<EntropySinkTag>(cand));
                if (rank >= bestAcceptRank) continue;
                var d = DecideDeposit(tail, cand, itemID);
                if (d == InputDecision.Accept)
                {
                    bestAcceptRank = rank;
                    building       = cand;
                    deliverDir     = dir;
                    decision       = d;
                }
                else if (d == InputDecision.Overflow && rank < bestOverflowRank)
                {
                    bestOverflowRank = rank;
                    overflowBldg     = cand;
                    overflowDir      = dir;
                }
            }
            if (building == Entity.Null && overflowBldg != Entity.Null)
            {
                building   = overflowBldg;
                deliverDir = overflowDir;
                decision   = InputDecision.Overflow;
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
