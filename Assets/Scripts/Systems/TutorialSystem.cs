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
                if (SaveManager.Instance != null &&
                    SaveManager.Instance.Current.unlockedResearch.Contains(step.skipCondition.researchId))
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
                    return SaveManager.Instance != null &&
                           SaveManager.Instance.Current.unlockedResearch.Contains(c.researchId);

                case ConditionType.BuildingMin:
                    int buildingCount = 0;
                    foreach (var _ in SystemAPI.Query<RefRO<BuildingData>>())
                        buildingCount++;
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

        private static int FindStepIndex(TutorialFlowSO flow, string id)
        {
            if (string.IsNullOrEmpty(id) || flow.steps == null) return -1;
            for (int i = 0; i < flow.steps.Length; i++)
                if (flow.steps[i].id == id) return i;
            return -1;
        }

    }
}
