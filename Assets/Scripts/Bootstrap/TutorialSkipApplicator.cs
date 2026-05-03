#if UNITY_EDITOR
using Unity.Entities;
using UnityEditor;
using UnityEngine;

namespace MobileIdleBuilder.Dev
{
    // ── Phase 1: ECS (InitializationSystemGroup) ──────────────────────────────
    // Runs before SimulationSystemGroup (TutorialSystem) every frame until the
    // ECS singletons exist. Sets tutorial step index and starting entropy.
    // Research/recipe unlocks are NOT done here — ResearchService.Start() may
    // not have run yet, so its internal _unlockedIds set would be stale.

    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct TutorialSkipSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<TutorialStateData>();
            state.RequireForUpdate<PlayerProgressData>();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (!EditorPrefs.GetBool("TutorialSkip.Enabled", false))
            {
                state.Enabled = false;
                return;
            }

            int stepIdx = EditorPrefs.GetInt("TutorialSkip.StepIndex", 0);
            long.TryParse(EditorPrefs.GetString("TutorialSkip.Entropy", "0"), out long entropy);

            var tState = SystemAPI.GetSingleton<TutorialStateData>();
            tState.CurrentStepIndex = stepIdx;
            SystemAPI.SetSingleton(tState);

            var pState = SystemAPI.GetSingleton<PlayerProgressData>();
            pState.BaseCurrency = entropy;
            SystemAPI.SetSingleton(pState);

            Debug.Log($"[TutorialSkip] Step → {stepIdx}, Entropy → {entropy}");
            state.Enabled = false;
        }
    }

    // ── Phase 2: MonoBehaviour (DefaultExecutionOrder 100) ────────────────────
    // Created at AfterSceneLoad; its Start() fires after ResearchService.Start()
    // (order 0) and RecipeKnowledgeService.Start() (order -70) have both run.
    // Calls ResearchService.ForceUnlock() for every research-gated step that was
    // skipped — ForceUnlock updates _unlockedIds, SaveData, and recipe knowledge
    // in a single call, regardless of subscene streaming timing.

    static class TutorialSkipApplicator
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (!EditorPrefs.GetBool("TutorialSkip.Enabled", false)) return;
            var go = new GameObject("_TutorialSkipApplicator");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<TutorialSkipBehaviour>();
        }
    }

    [DefaultExecutionOrder(100)]
    sealed class TutorialSkipBehaviour : MonoBehaviour
    {
        void Start()
        {
            var flow = TutorialFlowSO.Current;
            var rs   = ResearchService.Instance;
            Debug.Log($"[TutorialSkip] Applicator Start — flow={(flow == null ? "NULL" : "OK")}, ResearchService={(rs == null ? "NULL" : "OK")}");
            if (flow == null || rs == null) { Destroy(gameObject); return; }

            int stepIdx = EditorPrefs.GetInt("TutorialSkip.StepIndex", 0);

            for (int i = 0; i < stepIdx && i < flow.steps.Length; i++)
            {
                var cond = flow.steps[i].advanceCondition;
                if (cond?.type == ConditionType.ResearchUnlocked &&
                    !string.IsNullOrEmpty(cond.researchId))
                {
                    rs.ForceUnlock(cond.researchId);
                }
            }

            Destroy(gameObject);
        }
    }
}
#endif
