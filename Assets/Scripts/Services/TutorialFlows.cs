using System.Collections.Generic;
using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// One tutorial per world. Physics runs the main tutorial (<see cref="TutorialFlowSO.Default"/>, saved in
    /// <c>SaveData.tutorial</c>); a world with its own <see cref="WorldSO.tutorialFlow"/> (Chemistry) runs
    /// that one while it is live, saved per world in <c>SaveData.worldTutorials</c>. Both share the one
    /// <see cref="TutorialStateData"/> singleton, so the Physics tutorial never runs in Chemistry.
    /// <para>
    /// Order on a world switch: <c>SiteService.SwitchTo</c> saves first (FlushToSave calls <see cref="Store"/>
    /// while the old flow is still current), then <c>GridSaveService.LoadGrid</c> calls <see cref="Activate"/>
    /// for the new world. On scene load <c>ECSLoadBridge</c> resets to the default flow before applying the
    /// Physics state, and LoadGrid then switches to the live world's flow.
    /// </para>
    /// </summary>
    public static class TutorialFlows
    {
        /// <summary>True when the running flow is a world's own tutorial rather than the main one.</summary>
        public static bool WorldFlowActive => IsWorldFlow(TutorialFlowSO.Current);

        static bool IsWorldFlow(TutorialFlowSO f) => f != null && !string.IsNullOrEmpty(f.worldId);

        /// <summary>Switches the running flow to <paramref name="world"/>'s (or the main one) and loads its saved state.</summary>
        public static void Activate(EntityManager em, SaveData save, WorldSO world)
        {
            var target = world != null ? world.tutorialFlow : null;
            var resolved = target != null ? target : TutorialFlowSO.Default;
            if (resolved == TutorialFlowSO.Current) return;

            TutorialFlowSO.Activate(target);
            using var q = em.CreateEntityQuery(ComponentType.ReadWrite<TutorialStateData>());
            if (q.IsEmpty || save == null) return;
            var ts = q.GetSingleton<TutorialStateData>();
            Load(ref ts, save, TutorialFlowSO.Current);
            q.SetSingleton(ts);
            GameLogger.Info($"[Tutorial] Flow → '{TutorialFlowSO.Current?.name}' step {ts.CurrentStepIndex} active={ts.IsActive}");
        }

        /// <summary>Fills <paramref name="ts"/> from the save slot of <paramref name="flow"/> (pure; tested).</summary>
        public static void Load(ref TutorialStateData ts, SaveData save, TutorialFlowSO flow)
        {
            int count = flow?.steps?.Length ?? 0;
            if (IsWorldFlow(flow))
            {
                var slot = Slot(save, flow.worldId, create: false);
                bool done = slot != null && slot.done;
                ts.IsActive         = !done && count > 0;
                ts.CurrentStepIndex = done ? count : IndexOf(flow, slot?.currentStepId);
                return;
            }
            // Main tutorial: the same rules ECSLoadBridge applies on load.
            if (save.tutorial.hasCompletedFirstRun)
            {
                ts.IsActive = false;
                ts.FirstRunComplete = true;
                return;
            }
            ts.IsActive         = save.tutorial.isActive;
            ts.CurrentStepIndex = IndexOf(flow, save.tutorial.currentStepId);
        }

        /// <summary>
        /// Writes the running world flow's state into its save slot. Returns false when the main tutorial is
        /// running, so the caller saves <c>SaveData.tutorial</c> as before.
        /// </summary>
        public static bool Store(SaveData save, TutorialStateData ts)
        {
            var flow = TutorialFlowSO.Current;
            if (!IsWorldFlow(flow) || save == null) return false;
            var slot = Slot(save, flow.worldId, create: true);
            int count = flow.steps?.Length ?? 0;
            slot.done = !ts.IsActive || ts.CurrentStepIndex >= count;
            if (ts.CurrentStepIndex < count) slot.currentStepId = flow.steps[ts.CurrentStepIndex].id;
            return true;
        }

        public static WorldTutorialSaveData Slot(SaveData save, string worldId, bool create)
        {
            save.worldTutorials ??= new List<WorldTutorialSaveData>();
            foreach (var s in save.worldTutorials)
                if (s != null && s.worldId == worldId) return s;
            if (!create) return null;
            var n = new WorldTutorialSaveData { worldId = worldId };
            save.worldTutorials.Add(n);
            return n;
        }

        static int IndexOf(TutorialFlowSO flow, string stepId)
        {
            if (flow?.steps == null || string.IsNullOrEmpty(stepId)) return 0;
            for (int i = 0; i < flow.steps.Length; i++)
                if (flow.steps[i]?.id == stepId) return i;
            return 0;
        }
    }
}
