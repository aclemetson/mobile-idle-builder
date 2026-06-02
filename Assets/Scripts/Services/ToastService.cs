using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Singleton service for surfacing "locked action" feedback to the player.
    /// Auto-creates itself at startup — no manual scene placement required.
    ///
    /// Feedback is delivered through InventoryPopupController.NotifyWarning so it
    /// appears as amber floating text at the same anchor as "+1 Item" pickups.
    ///
    /// Two calling patterns:
    ///   1. ToastService.Instance?.Post("quark_field")
    ///      Looks up the current tutorial step's lockedMessages for a matching triggerId
    ///      and shows the associated warning popup. Silent no-op on no match.
    ///   2. ToastService.Instance?.Post("message text")
    ///      Shows the message directly, bypassing tutorial lookup.
    ///      Use this for non-tutorial contexts (research lock, recipe gating, etc.).
    /// </summary>
    public class ToastService : SingletonMonoBehaviour<ToastService>
    {
        private EntityQuery _tutorialQuery;
        private bool        _ecsReady;

        // ── Unity lifecycle ───────────────────────────────────────────────────

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;
            _tutorialQuery = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<TutorialStateData>()
            );
            _ecsReady = true;
        }

        protected override void OnDestroy()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated && _ecsReady)
                _tutorialQuery.Dispose();
            base.OnDestroy();
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns true if the current tutorial step has a locked message for triggerId.
        /// Does not show any popup — use Post() to also show the message.
        /// </summary>
        public bool IsLocked(string triggerId)
        {
            if (string.IsNullOrEmpty(triggerId)) return false;
            var flow = TutorialFlowSO.Current;
            if (flow == null || !_ecsReady || _tutorialQuery.IsEmpty) return false;
            var state = _tutorialQuery.GetSingleton<TutorialStateData>();
            if (!state.IsActive) return false;
            int idx = state.CurrentStepIndex;
            if (idx >= flow.steps.Length) return false;
            var msgs = flow.steps[idx].onEnter?.lockedMessages;
            if (msgs == null) return false;
            foreach (var msg in msgs)
                if (msg?.triggerId == triggerId) return true;
            return false;
        }

        /// <summary>
        /// Looks up the current tutorial step's lockedMessages for a matching triggerId
        /// and shows the warning popup if found. Silent no-op if no match.
        /// </summary>
        public void Post(string triggerId)
        {
            if (string.IsNullOrEmpty(triggerId)) return;
            var flow = TutorialFlowSO.Current;
            if (flow == null || !_ecsReady || _tutorialQuery.IsEmpty) return;

            var state = _tutorialQuery.GetSingleton<TutorialStateData>();
            if (!state.IsActive) return;

            int idx = state.CurrentStepIndex;
            if (idx >= flow.steps.Length) return;

            var msgs = flow.steps[idx].onEnter?.lockedMessages;
            if (msgs == null || msgs.Length == 0) return;

            foreach (var msg in msgs)
            {
                if (msg?.triggerId != triggerId) continue;
                InventoryPopupController.NotifyWarning(msg.message);
                return;
            }
        }
    }
}
