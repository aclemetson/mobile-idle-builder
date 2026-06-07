using System.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Watches TutorialStateData.CurrentStepIndex each frame and drives all tutorial UI.
    /// Step definitions — dialogues, highlights, hints, collection gates — are read entirely
    /// from TutorialFlowSO.Current (generated from game_data.json). No step names live here.
    ///
    /// UI events that advance UiEvent-conditioned steps are routed through TryAdvanceOnUiEvent().
    /// On every step advance the current state is written to tutorial_state.json.
    /// </summary>
    public class TutorialOverlayController : MonoBehaviour
    {
        [SerializeField] private TutorialFlowSO          tutorialFlow;
        [SerializeField] private DialogueController      dialogueController;
        [SerializeField] private UIDocument              uiDocument;
        [SerializeField] private TutorialHighlighter     tutorialHighlighter;
        [SerializeField] private MaxwellsDemonController maxwellsDemon;
        [SerializeField] private HUDController           hudController;

        private int    _lastStepIndex = -1;
        private bool   _wasActive;
        private Button _pulsingButton;
        private Coroutine _pulseRoutine;

        private EntityQuery _tutorialQuery;
        private bool        _queryReady;

        void Start()
        {
            if (dialogueController != null)
            {
                dialogueController.OnDialogueComplete   += OnDialogueComplete;
                dialogueController.OnHighlightRequested += OnHighlightRequested;
                dialogueController.OnActionTriggered    += OnActionTriggered;
            }

            if (hudController == null)
                hudController = FindAnyObjectByType<HUDController>();

            if (hudController != null)
            {
                hudController.OnDrawerOpened        += OnDrawerOpenedHandler;
                hudController.OnResearchPanelOpened += OnResearchPanelOpened;
                hudController.OnRecipePanelOpened   += OnRecipePanelOpenedHandler;
            }

            if (maxwellsDemon == null)
                maxwellsDemon = FindAnyObjectByType<MaxwellsDemonController>();

            if (maxwellsDemon != null)
            {
                maxwellsDemon.OnOpened         += OnDemonOpened;
                maxwellsDemon.OnItemsDeposited += OnDemonItemsDeposited;
                maxwellsDemon.OnClosed         += OnDemonClosed;
            }

            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null)
            {
                _tutorialQuery = world.EntityManager.CreateEntityQuery(
                    ComponentType.ReadWrite<TutorialStateData>());
                _queryReady = true;
            }
        }

        void OnDestroy()
        {
            if (dialogueController != null)
            {
                dialogueController.OnDialogueComplete   -= OnDialogueComplete;
                dialogueController.OnHighlightRequested -= OnHighlightRequested;
                dialogueController.OnActionTriggered    -= OnActionTriggered;
            }

            if (hudController != null)
            {
                hudController.OnDrawerOpened        -= OnDrawerOpenedHandler;
                hudController.OnResearchPanelOpened -= OnResearchPanelOpened;
                hudController.OnRecipePanelOpened   -= OnRecipePanelOpenedHandler;
            }

            if (maxwellsDemon != null)
            {
                maxwellsDemon.OnOpened         -= OnDemonOpened;
                maxwellsDemon.OnItemsDeposited -= OnDemonItemsDeposited;
                maxwellsDemon.OnClosed         -= OnDemonClosed;
            }
        }

        void Update()
        {
            if (!_queryReady || _tutorialQuery.IsEmpty) return;

            // Don't react until ECSLoadBridge has applied saved progress to ECS.
            // Without this guard the baked step-0 default fires OnStepChanged before
            // the saved step index is written, replaying the intro dialogue on every load.
            var bridge = ECSLoadBridge.Instance;
            if (bridge != null && !bridge.IsLoaded) return;

            var state = _tutorialQuery.GetSingleton<TutorialStateData>();

            // Tutorial just ended — clean up all tutorial UI and stop watching.
            if (_wasActive && !state.IsActive)
            {
                _wasActive = false;
                UnlockAllFields();
                StopPulseRoutine();
                tutorialHighlighter?.ClearHighlight();
                // Don't clear the hint when ended by prestige — HUDController shows the
                // achievements unlock hint on the same frame and we must not stomp it.
                if (!(SaveManager.Instance?.Current?.tutorial.hasCompletedFirstRun ?? false))
                    hudController?.HideTutorialHint();
                dialogueController?.HideDialogue();
                return;
            }

            if (!state.IsActive) return;
            _wasActive = true;

            if (state.CurrentStepIndex == _lastStepIndex) return;

            int prevIdx = _lastStepIndex;
            _lastStepIndex = state.CurrentStepIndex;
            OnStepChanged(state.CurrentStepIndex, prevIdx);
        }

        // ── Step entry ────────────────────────────────────────────────────────

        private void OnStepChanged(int stepIndex, int prevStepIndex = -1)
        {
            StopPulseRoutine();
            tutorialHighlighter?.ClearHighlight();
            hudController?.HideTutorialHint();

            // Checkpoint: persist the new step immediately rather than waiting for auto-save.
            SaveManager.Instance?.SaveLocal();

            var flow = TutorialFlowSO.Current;
            if (flow == null || stepIndex >= flow.steps.Length) return;

            var step = flow.steps[stepIndex];

            // When an inventory goal was just met, flash the next instruction as a popup
            // so the player knows both that the goal is done and what to do next.
            if (!string.IsNullOrEmpty(step.hintText)
                && prevStepIndex >= 0 && prevStepIndex < flow.steps.Length)
            {
                var prevCond = flow.steps[prevStepIndex].advanceCondition;
                if (prevCond?.type == ConditionType.InventoryMin
                    || prevCond?.type == ConditionType.InventoryZero)
                    hudController?.ShowNotification("→", step.hintText);
            }

            if (!string.IsNullOrEmpty(step.hintText))
                hudController?.ShowTutorialHint(step.hintText);

            ApplyOnEnterActions(step.onEnter);
        }

        private void ApplyOnEnterActions(TutorialOnEnter enter)
        {
            if (enter == null) return;

            ApplyFieldLockStates(enter);

            // Dialogue
            if (enter.dialogue != null)
                dialogueController?.PlayDialogue(enter.dialogue);

            // World highlight
            switch (enter.highlightMode)
            {
                case HighlightMode.Full:
                    tutorialHighlighter?.ShowHighlight(enter.highlightTarget);
                    break;
                case HighlightMode.VisualOnly:
                    tutorialHighlighter?.ShowVisualOnly(enter.highlightTarget);
                    break;
            }

            // UI button pulse
            if (!string.IsNullOrEmpty(enter.pulseButtonId))
                StartPulseButton(enter.pulseButtonId);

            // Maxwell's Demon panel item highlights
            if (enter.demonHighlightItemIds != null)
                foreach (var id in enter.demonHighlightItemIds)
                    maxwellsDemon?.HighlightTutorialItem(id);
        }

        // ── UI event routing ─────────────────────────────────────────────────

        /// <summary>
        /// Advances the current step if it is waiting for the named UI event.
        /// Safe to call speculatively — no-ops if the current step expects a different event.
        /// </summary>
        private void TryAdvanceOnUiEvent(string eventId)
        {
            if (!_queryReady || _tutorialQuery.IsEmpty) return;
            var flow = TutorialFlowSO.Current;
            if (flow == null) return;

            var state = _tutorialQuery.GetSingleton<TutorialStateData>();
            if (!state.IsActive || state.CurrentStepIndex >= flow.steps.Length) return;

            var cond = flow.steps[state.CurrentStepIndex].advanceCondition;
            if (cond == null || cond.type != ConditionType.UiEvent) return;
            if (cond.uiEventId != eventId) return;

            AdvanceECSStep();
        }

        private void AdvanceECSStep()
        {
            if (!_queryReady || _tutorialQuery.IsEmpty) return;

            var state = _tutorialQuery.GetSingleton<TutorialStateData>();
            var flow  = TutorialFlowSO.Current;

            state.CurrentStepIndex++;
            if (flow == null || state.CurrentStepIndex >= flow.steps.Length)
                state.IsActive = false;

            _tutorialQuery.SetSingleton(state);

            _lastStepIndex = state.CurrentStepIndex;
            OnStepChanged(state.CurrentStepIndex);
        }

        // ── Dialogue event handlers ───────────────────────────────────────────

        private void OnDialogueComplete() => TryAdvanceOnUiEvent("dialogue_complete");

        private void OnHighlightRequested(string target)
        {
            if (string.IsNullOrEmpty(target))
            {
                tutorialHighlighter?.ClearHighlight();
                return;
            }
            if (target == "btn-research")
                StartPulseButton("btn-research");
            else
                tutorialHighlighter?.ShowHighlight(target);
        }

        private void OnActionTriggered(string action)
        {
            if (string.IsNullOrEmpty(action)) return;
            GameLogger.Develop($"[TutorialOverlay] Action: {action}");
        }

        // ── Maxwell's Demon event handlers ────────────────────────────────────

        private void OnDemonOpened()
        {
            tutorialHighlighter?.ClearHighlight();
            TryAdvanceOnUiEvent("demon_opened");
        }

        private void OnDemonItemsDeposited()
        {
            if (!_queryReady || _tutorialQuery.IsEmpty) return;
            var flow = TutorialFlowSO.Current;
            if (flow == null) return;

            var state = _tutorialQuery.GetSingleton<TutorialStateData>();
            if (!state.IsActive || state.CurrentStepIndex >= flow.steps.Length) return;

            // Clear demon panel item highlights as soon as any deposit occurs
            var enter = flow.steps[state.CurrentStepIndex].onEnter;
            if (enter?.demonHighlightItemIds != null && enter.demonHighlightItemIds.Length > 0)
                maxwellsDemon?.ClearTutorialHighlight();
        }

        private void OnDemonClosed() => TryAdvanceOnUiEvent("demon_closed");

        private void OnDrawerOpenedHandler()        => TryAdvanceOnUiEvent("drawer_opened");
        private void OnResearchPanelOpened()         => TryAdvanceOnUiEvent("research_panel_opened");
        private void OnRecipePanelOpenedHandler()    => TryAdvanceOnUiEvent("recipe_panel_opened");

        /// <summary>
        /// Called by the building upgrade UI when the player purchases a speed upgrade on the
        /// Atom Generator (atomic_assembler). Advances the tutorial step waiting on
        /// "atom_generator_speed_upgraded".
        /// </summary>
        public void NotifyAtomGeneratorSpeedUpgraded() =>
            TryAdvanceOnUiEvent("atom_generator_speed_upgraded");

        // ── Field lock states ─────────────────────────────────────────────────

        /// <summary>
        /// Dims fields that are not accessible in the given step and restores those that are.
        /// - blockCollection=true  → all fields locked
        /// - collectionFilter set  → fields of the filtered type unlocked, all others locked
        /// - neither               → all fields unlocked
        /// </summary>
        private void ApplyFieldLockStates(TutorialOnEnter enter)
        {
            var allFields = FindObjectsByType<FieldInstance>(FindObjectsSortMode.None);
            foreach (var fi in allFields)
            {
                if (fi.Field == null) continue;
                bool locked;
                if (enter.blockCollection)
                    locked = true;
                else if (enter.collectionFilter != FieldType.None)
                    locked = fi.Field.fieldType != enter.collectionFilter;
                else
                    locked = false;
                fi.SetLocked(locked);
            }
        }

        private void UnlockAllFields()
        {
            var allFields = FindObjectsByType<FieldInstance>(FindObjectsSortMode.None);
            foreach (var fi in allFields)
                fi.SetLocked(false);
        }

        // ── UI helpers ────────────────────────────────────────────────────────

        private void StartPulseButton(string buttonId)
        {
            if (uiDocument == null) return;
            var btn = uiDocument.rootVisualElement.Q<Button>(buttonId);
            if (btn == null) return;
            _pulsingButton = btn;
            _pulseRoutine  = StartCoroutine(PulseButton(btn));
        }

        private void StopPulseRoutine()
        {
            if (_pulseRoutine != null) { StopCoroutine(_pulseRoutine); _pulseRoutine = null; }
            if (_pulsingButton != null)
            {
                _pulsingButton.RemoveFromClassList("panel-btn--highlight");
                _pulsingButton = null;
            }
        }

        private IEnumerator PulseButton(Button btn)
        {
            while (true)
            {
                btn.AddToClassList("panel-btn--highlight");
                yield return new WaitForSecondsRealtime(0.7f);
                btn.RemoveFromClassList("panel-btn--highlight");
                yield return new WaitForSecondsRealtime(0.7f);
            }
        }

    }
}
