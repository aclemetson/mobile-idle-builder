using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Advances the tutorial step index when each step's completion condition is met.
    /// Reads step definitions from TutorialFlowSO.Current — all step logic is data-driven.
    /// Does NOT drive UI directly; TutorialOverlayController watches CurrentStepIndex.
    ///
    /// Item IDs used for inventory condition checks (must match recipes.json):
    ///   1 = Up Quark | 2 = Down Quark | 3 = Electron
    ///   4 = Proton   | 5 = Neutron    | 6 = Hydrogen
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProductionSystem))]
    public partial struct TutorialSystem : ISystem
    {
        private int _diagLastCount;
        private int _diagLastStep;

        /// <summary>
        /// Linked sources counted for the current step when it advances on ConveyorLinked, else -1. Lets
        /// the overlay tell a belt that linked nothing apart from one that linked but the step wants more
        /// (#255: a wrong belt left the step silently stuck).
        /// </summary>
        public static int CurrentStepConveyorLinks { get; private set; } = -1;

        public void OnCreate(ref SystemState state)
        {
            _diagLastCount = -1;
            _diagLastStep  = -1;
            state.RequireForUpdate<TutorialStateData>();
            state.RequireForUpdate<PlayerInventoryTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            CurrentStepConveyorLinks = -1;
            var tutorial = SystemAPI.GetSingleton<TutorialStateData>();
            if (!tutorial.IsActive) return;

            var flow = TutorialFlowSO.Current;
            if (flow == null || flow.steps == null || flow.steps.Length == 0)
            {
                GameLogger.Warning("[TutorialSystem] TutorialFlowSO.Current is null or empty — cannot evaluate.");
                return;
            }

            // Tutorial complete — index ran past the last step
            if (tutorial.CurrentStepIndex >= flow.steps.Length)
            {
                tutorial.IsActive = false;
                SystemAPI.SetSingleton(tutorial);
                return;
            }

            var step = flow.steps[tutorial.CurrentStepIndex];

            if (tutorial.CurrentStepIndex != _diagLastStep)
            {
                _diagLastStep  = tutorial.CurrentStepIndex;
                _diagLastCount = -1;
                GameLogger.Develop($"[TutorialSystem] Now evaluating step {tutorial.CurrentStepIndex}: '{step.id}'  condition={step.advanceCondition?.type}");
            }

            // Check skip condition before the normal advance condition.
            // Lets returning players who already have research jump ahead automatically.
            if (step.skipCondition != null && !string.IsNullOrEmpty(step.skipCondition.researchId))
            {
                if (IsResearchUnlocked(step.skipCondition.researchId))
                {
                    int skipIdx = FindStepIndex(flow, step.skipCondition.skipToId);
                    if (skipIdx >= 0)
                    {
                        tutorial.CurrentStepIndex = skipIdx;
                        SystemAPI.SetSingleton(tutorial);
                    }
                    return;
                }
            }

            var inventory = SystemAPI.GetSingletonBuffer<InventorySlot>(true); // read-only

            // Smart progression: if this is a substep and a LATER substep in the same
            // contiguous run already has its advance condition satisfied, this substep is
            // moot — e.g. the protons it asked for were already consumed into hydrogen, so
            // its own InventoryMin will never read high enough. Skip it. The run is bounded
            // by the first non-substep ("major") step, which is never auto-skipped this way.
            // Advancing one index per frame cascades forward until the satisfied substep is
            // reached and advances on its own condition.
            if (step.isSubstep && LaterSubstepSatisfied(flow, tutorial.CurrentStepIndex, inventory, ref state))
            {
                tutorial.CurrentStepIndex++;
                SystemAPI.SetSingleton(tutorial);
                return;
            }

            // Diagnostic: log inventory count changes for InventoryMin steps.
            if (step.advanceCondition != null && step.advanceCondition.type == ConditionType.InventoryMin
                && step.advanceCondition.items != null && step.advanceCondition.items.Count > 0)
            {
                var req  = step.advanceCondition.items[0];
                int have = SlotBufferUtils.CountInInventory(inventory, req.itemId);
                if (have != _diagLastCount)
                {
                    _diagLastCount = have;
                    GameLogger.Develop($"[TutorialSystem] Step '{step.id}' — item {req.itemId}: {have}/{req.quantity}  IsActive={tutorial.IsActive}  FlowSteps={flow.steps.Length}");
                }
            }

            if (step.advanceCondition != null && step.advanceCondition.type == ConditionType.ConveyorLinked)
            {
                CurrentStepConveyorLinks = CountConveyorLinks(step.advanceCondition, ref state);
                if (CurrentStepConveyorLinks < System.Math.Max(1, step.advanceCondition.minCount)) return;
            }
            else if (!EvaluateCondition(step.advanceCondition, inventory, ref state)) return;

            // Side-effect: mark first run complete when the prestige-run condition triggers
            if (step.advanceCondition != null && step.advanceCondition.type == ConditionType.PrestigeRunMin)
                tutorial.FirstRunComplete = true;

            tutorial.CurrentStepIndex++;

            if (tutorial.CurrentStepIndex >= flow.steps.Length)
                tutorial.IsActive = false;

            SystemAPI.SetSingleton(tutorial);

            string nextId = tutorial.IsActive && tutorial.CurrentStepIndex < flow.steps.Length
                ? flow.steps[tutorial.CurrentStepIndex].id
                : "(complete)";
            GameLogger.Debug($"[TutorialSystem] Advanced to step {tutorial.CurrentStepIndex}: {nextId}");
        }

        /// <summary>
        /// True if any later substep in the contiguous run beginning after <paramref name="startIndex"/>
        /// already has its advance condition satisfied. The scan stops at the first non-substep
        /// ("major") step so a run of substeps never lets the tutorial jump past a major beat.
        /// UiEvent conditions never count as satisfied here (they only advance via explicit UI calls),
        /// so forward-skip fires only on state-derived conditions like InventoryMin/InventoryZero.
        /// </summary>
        private bool LaterSubstepSatisfied(TutorialFlowSO flow, int startIndex,
            DynamicBuffer<InventorySlot> inv, ref SystemState state)
        {
            for (int j = startIndex + 1; j < flow.steps.Length; j++)
            {
                var later = flow.steps[j];
                if (later == null || !later.isSubstep) break; // major step bounds the run
                if (EvaluateCondition(later.advanceCondition, inv, ref state)) return true;
            }
            return false;
        }

        // ── Condition evaluator ───────────────────────────────────────────────

        private bool EvaluateCondition(TutorialConditionDef c,
            DynamicBuffer<InventorySlot> inv, ref SystemState state)
        {
            if (c == null) return true; // null condition = auto-advance

            switch (c.type)
            {
                case ConditionType.Auto:
                    return true;

                case ConditionType.UiEvent:
                    // Always UI-driven — MonoBehaviour calls AdvanceStep via TutorialOverlayController
                    return false;

                case ConditionType.InventoryMin:
                    if (c.items == null || c.items.Count == 0) return true;
                    if (c.anyOf)
                    {
                        foreach (var r in c.items)
                            if (SlotBufferUtils.CountInInventory(inv, r.itemId) >= r.quantity) return true;
                        return false;
                    }
                    else
                    {
                        foreach (var r in c.items)
                            if (SlotBufferUtils.CountInInventory(inv, r.itemId) < r.quantity) return false;
                        return true;
                    }

                case ConditionType.InventoryZero:
                    if (c.items == null || c.items.Count == 0) return true;
                    foreach (var r in c.items)
                        if (SlotBufferUtils.CountInInventory(inv, r.itemId) > 0) return false;
                    return true;

                case ConditionType.ResearchUnlocked:
                    return IsResearchUnlocked(c.researchId);

                case ConditionType.BuildingMin:
                    int buildingCount = 0;
                    foreach (var bd in SystemAPI.Query<RefRO<BuildingData>>())
                    {
                        // buildingType < 0 = count all; otherwise only matching building types.
                        if (c.buildingType < 0 || bd.ValueRO.BuildingType == c.buildingType)
                            buildingCount++;
                    }
                    return buildingCount >= c.minCount;

                case ConditionType.PrestigeRunMin:
                    return SystemAPI.GetSingleton<PrestigeData>().RunCount >= c.minCount;

                case ConditionType.PrestigeAvailable:
                    return SystemAPI.GetSingleton<PlayerProgressData>().PrestigeAvailable;

                case ConditionType.ConveyorLinked:
                    return CountConveyorLinks(c, ref state) >= System.Math.Max(1, c.minCount);

                case ConditionType.BuildingUpgradeMin:
                    foreach (var (bd, e) in SystemAPI.Query<RefRO<BuildingData>>().WithEntityAccess())
                        if (MatchesTarget(c, bd.ValueRO, e, ref state) && bd.ValueRO.UpgradeLevel >= c.minLevel)
                            return true;
                    return false;

                case ConditionType.FieldPurchasedMin:
                    return FieldPurchaseService.PurchasedCount(SaveManager.Instance?.Current, c.fieldId)
                           >= System.Math.Max(1, c.minCount);

                default:
                    return false;
            }
        }

        // ── Belt links ────────────────────────────────────────────────────────

        /// <summary>
        /// True when <paramref name="e"/> is the condition's target building: Maxwell's Demon by its
        /// EntropySinkTag, anything else by BuildingType (-1 = any building).
        /// </summary>
        private bool MatchesTarget(TutorialConditionDef c, BuildingData bd, Entity e, ref SystemState state)
        {
            if (c.buildingIsEntropySink) return SystemAPI.HasComponent<EntropySinkTag>(e);
            return c.buildingType < 0 || bd.BuildingType == c.buildingType;
        }

        /// <summary>
        /// Number of distinct source buildings whose output belt currently ends in front of an input
        /// of a target building (#233). Read from the live belts, so the order the player built them
        /// in doesn't matter. Port fronts follow ConveyorSystem (see TutorialRouteGuide).
        /// </summary>
        private int CountConveyorLinks(TutorialConditionDef c, ref SystemState state)
        {
            var sourceFronts = new List<(int, Vector2Int)>();
            var inputFronts  = new HashSet<Vector2Int>();

            foreach (var (bd, ports, e) in SystemAPI.Query<RefRO<BuildingData>, DynamicBuffer<PlacedPortData>>()
                                                     .WithEntityAccess())
            {
                bool isTarget = MatchesTarget(c, bd.ValueRO, e, ref state);
                bool isSource = !isTarget && !SystemAPI.HasComponent<EntropySinkTag>(e)
                             && (c.fromBuildingType < 0 || bd.ValueRO.BuildingType == c.fromBuildingType);
                if (!isTarget && !isSource) continue;

                for (int i = 0; i < ports.Length; i++)
                {
                    var p      = ports[i];
                    var cell   = new Vector2Int(p.CellX, p.CellY);
                    var facing = (OutputDirection)p.Facing;
                    if (isTarget && p.PortType == (int)PortType.Input)
                        inputFronts.Add(TutorialRouteGuide.InputFront(cell, facing));
                    else if (isSource && p.PortType == (int)PortType.Output)
                        sourceFronts.Add((e.Index, TutorialRouteGuide.OutputFront(cell, facing)));
                }
            }
            if (sourceFronts.Count == 0 || inputFronts.Count == 0) return 0;

            var belts = new Dictionary<Vector2Int, Vector2Int?>();
            foreach (var seg in SystemAPI.Query<RefRO<ConveyorSegmentData>>())
            {
                var s = seg.ValueRO;
                Vector2Int? next = null;
                if (s.NextSegment != Entity.Null && SystemAPI.HasComponent<ConveyorSegmentData>(s.NextSegment))
                {
                    var n = SystemAPI.GetComponent<ConveyorSegmentData>(s.NextSegment).Cell;
                    next = new Vector2Int(n.x, n.y);
                }
                belts[new Vector2Int(s.Cell.x, s.Cell.y)] = next;
            }

            return TutorialRouteGuide.CountLinkedSources(sourceFronts, inputFronts, belts);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// True if the given research is unlocked. Prefers ResearchService's live
        /// in-memory unlock set (updated unconditionally on every purchase — the same
        /// source the research-panel "purchased" checkmark uses), and only falls back
        /// to the persisted save list if the service is unavailable. Reading the live
        /// service avoids a stall where a purchase shows as done in the UI but the
        /// save layer (SaveManager.Current) is absent or lagging, leaving a research-
        /// gated tutorial step unable to advance.
        /// </summary>
        private static bool IsResearchUnlocked(string researchId)
        {
            if (string.IsNullOrEmpty(researchId)) return false;

            if (ResearchService.Instance != null)
                return ResearchService.Instance.IsUnlocked(researchId);

            var save = SaveManager.Instance?.Current;
            return save?.unlockedResearch != null &&
                   save.unlockedResearch.Contains(researchId);
        }

        private static int FindStepIndex(TutorialFlowSO flow, string id)
        {
            if (string.IsNullOrEmpty(id) || flow.steps == null) return -1;
            for (int i = 0; i < flow.steps.Length; i++)
                if (flow.steps[i].id == id) return i;
            return -1;
        }

    }
}
