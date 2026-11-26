using System;
using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Which research the player may buy during the tutorial. Buying a research out of order on a step
    /// where field collection is paused spent the entropy the tutorial's own research needed, and with
    /// the fields locked (and the Demon taking nothing, <see cref="TutorialSellPolicy"/>) there was no way
    /// to earn it back.
    ///
    /// While collection is restricted (every step from the current one up to the next unfinished
    /// <see cref="ConditionType.ResearchUnlocked"/> step pauses or filters collection), only that step's
    /// research can be bought. Once collection is open, or outside the tutorial, anything can.
    /// A save already stuck (on that research step, unable to afford it) gets it for what entropy it has
    /// (<see cref="IsRescue"/>).
    /// </summary>
    public static class TutorialResearchPolicy
    {
        public const string BlockedMessage = "Finish the tutorial's research first.";

        /// <summary>
        /// The research the tutorial needs next while collection is restricted, or null when any research
        /// may be bought.
        /// </summary>
        public static string RequiredResearch(TutorialFlowSO flow, bool active, int stepIndex,
                                              Func<string, bool> isUnlocked)
        {
            if (!active || flow?.steps == null || stepIndex < 0) return null;
            for (int i = stepIndex; i < flow.steps.Length; i++)
            {
                var step = flow.steps[i];
                if (step == null) continue;
                if (!CollectionRestricted(step)) return null;

                var cond = step.advanceCondition;
                if (cond?.type != ConditionType.ResearchUnlocked || string.IsNullOrEmpty(cond.researchId)) continue;
                if (isUnlocked != null && isUnlocked(cond.researchId)) continue;
                return cond.researchId;
            }
            return null;
        }

        public static bool CanBuy(TutorialFlowSO flow, bool active, int stepIndex, string researchId,
                                  Func<string, bool> isUnlocked)
        {
            string required = RequiredResearch(flow, active, stepIndex, isUnlocked);
            return required == null || required == researchId;
        }

        /// <summary>
        /// True when the current step waits on <paramref name="researchId"/> with collection paused or limited
        /// to some fields, and the player cannot afford it: the purchase goes through for whatever entropy they
        /// hold. A limited step counts too: the fields it leaves open give items, not entropy, and the Demon
        /// takes nothing on a research step (<see cref="TutorialSellPolicy"/>).
        /// </summary>
        public static bool IsRescue(TutorialFlowSO flow, bool active, int stepIndex, string researchId,
                                    long balance, long cost)
        {
            if (!active || flow?.steps == null || stepIndex < 0 || stepIndex >= flow.steps.Length) return false;
            if (balance >= cost) return false;
            var step = flow.steps[stepIndex];
            if (step == null || !CollectionRestricted(step)) return false;
            var cond = step.advanceCondition;
            return cond?.type == ConditionType.ResearchUnlocked && cond.researchId == researchId;
        }

        static bool CollectionRestricted(TutorialStepDef step) =>
            step.onEnter != null && (step.onEnter.blockCollection || !FieldTypes.IsUnrestricted(step.onEnter.collectionFilter));

        // ── Live checks ────────────────────────────────────────────────────

        static bool TryState(out bool active, out int stepIndex)
        {
            active = false; stepIndex = -1;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return false;
            using var query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TutorialStateData>());
            if (query.CalculateEntityCount() != 1) return false;
            var state = query.GetSingleton<TutorialStateData>();
            active = state.IsActive; stepIndex = state.CurrentStepIndex;
            return true;
        }

        public static bool CanBuy(string researchId, Func<string, bool> isUnlocked) =>
            !TryState(out bool active, out int step) || CanBuy(TutorialFlowSO.Current, active, step, researchId, isUnlocked);

        public static bool IsRescue(string researchId, long balance, long cost) =>
            TryState(out bool active, out int step) && IsRescue(TutorialFlowSO.Current, active, step, researchId, balance, cost);
    }
}
