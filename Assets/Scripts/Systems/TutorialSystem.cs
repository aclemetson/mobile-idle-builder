using Unity.Entities;

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

        public void OnCreate(ref SystemState state)
        {
            _diagLastCount = -1;
            _diagLastStep  = -1;
            state.RequireForUpdate<TutorialStateData>();
            state.RequireForUpdate<PlayerInventoryTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
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

            if (!EvaluateCondition(step.advanceCondition, inventory, ref state)) return;

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

                default:
                    return false;
            }
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
