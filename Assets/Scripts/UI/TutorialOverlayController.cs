using System;
using System.Collections;
using System.IO;
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

            var state = _tutorialQuery.GetSingleton<TutorialStateData>();
            if (!state.IsActive) return;

            if (state.CurrentStepIndex == _lastStepIndex) return;

            _lastStepIndex = state.CurrentStepIndex;
            OnStepChanged(state.CurrentStepIndex);
        }

        // ── Step entry ────────────────────────────────────────────────────────

        private void OnStepChanged(int stepIndex)
        {
            StopPulseRoutine();
            tutorialHighlighter?.ClearHighlight();
            hudController?.HideTutorialHint();

            var flow = TutorialFlowSO.Current;
            if (flow == null || stepIndex >= flow.steps.Length) return;

            var step = flow.steps[stepIndex];

            if (!string.IsNullOrEmpty(step.hintText))
                hudController?.ShowTutorialHint(step.hintText);

            ApplyOnEnterActions(step.onEnter);
        }

        private void ApplyOnEnterActions(TutorialOnEnter enter)
        {
            if (enter == null) return;

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
            WriteTutorialState(state);
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
            Debug.Log($"[TutorialOverlay] Action: {action}");
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

        // ── State file ────────────────────────────────────────────────────────

        [Serializable]
        private class TutorialStateJson
        {
            public string currentStepId;
            public bool   isActive;
            public bool   firstRunComplete;
        }

        private static void WriteTutorialState(TutorialStateData s)
        {
            var flow = TutorialFlowSO.Current;
            string stepId = (flow != null && s.CurrentStepIndex < flow.steps.Length)
                ? flow.steps[s.CurrentStepIndex].id
                : "";

            var payload = JsonUtility.ToJson(new TutorialStateJson
            {
                currentStepId    = stepId,
                isActive         = s.IsActive,
                firstRunComplete = s.FirstRunComplete
            }, prettyPrint: true);

            try
            {
                File.WriteAllText(
                    Path.Combine(Application.persistentDataPath, "tutorial_state.json"),
                    payload);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TutorialOverlay] Could not write tutorial_state.json: {e.Message}");
            }
        }
    }
}
