using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Which items Maxwell's Demon will take by hand during the tutorial. Selling quarks on a craft step,
    /// where field collection is paused, used to leave the player unable to craft or collect.
    ///
    /// The rule covers the hand-crafting phase: every step up to the last sell step (the last one that
    /// advances on <see cref="ConditionType.InventoryZero"/>). A sell step takes only the items it asks
    /// for; any other step in the phase takes nothing. After the phase, and outside the tutorial,
    /// everything sells. Belt deliveries into the Demon never pass through here.
    /// </summary>
    public static class TutorialSellPolicy
    {
        public const string BlockedMessage = "Hold on to these, the tutorial needs them.";

        public static bool CanSell(TutorialFlowSO flow, bool active, int stepIndex, int itemId)
        {
            if (!active || flow?.steps == null || stepIndex < 0 || stepIndex >= flow.steps.Length) return true;
            if (stepIndex > LastSellStep(flow)) return true;

            var step = flow.steps[stepIndex];
            var cond = step?.advanceCondition;
            if (cond == null || cond.type != ConditionType.InventoryZero) return false;

            if (cond.items != null)
                foreach (var req in cond.items)
                    if (req != null && req.itemId == itemId) return true;

            var highlighted = step.onEnter?.demonHighlightItemIds;
            if (highlighted != null)
                foreach (var id in highlighted)
                    if (id == itemId) return true;

            return false;
        }

        /// <summary>Index of the last step that advances on an emptied inventory; -1 when there is none.</summary>
        public static int LastSellStep(TutorialFlowSO flow)
        {
            if (flow?.steps == null) return -1;
            for (int i = flow.steps.Length - 1; i >= 0; i--)
                if (flow.steps[i]?.advanceCondition?.type == ConditionType.InventoryZero) return i;
            return -1;
        }

        /// <summary>Live check against the running tutorial state.</summary>
        public static bool CanSell(int itemId)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return true;
            using var query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TutorialStateData>());
            if (query.CalculateEntityCount() != 1) return true;
            var state = query.GetSingleton<TutorialStateData>();
            return CanSell(TutorialFlowSO.Current, state.IsActive, state.CurrentStepIndex, itemId);
        }
    }
}
