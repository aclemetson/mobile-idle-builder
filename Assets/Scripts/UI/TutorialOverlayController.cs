using System.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Watches TutorialStateData each frame and drives tutorial-specific UI:
    ///   - Plays the intro dialogue sequence on IntroDialogue step
    ///   - Pulses the Research button on BuyRecombinationI step
    ///   - Shows hint notifications on later steps
    ///
    /// This MonoBehaviour advances the ECS TutorialStep when dialogue completes.
    /// </summary>
    public class TutorialOverlayController : MonoBehaviour
    {
        [SerializeField] private DialogueController      dialogueController;
        [SerializeField] private DialogueSO              introDialogue;
        [SerializeField] private UIDocument              uiDocument;
        [SerializeField] private TutorialHighlighter     tutorialHighlighter;
        [SerializeField] private MaxwellsDemonController maxwellsDemon;
        [SerializeField] private HUDController           hudController;

        private TutorialStep _lastStep    = TutorialStep.None;
        private Coroutine    _pulseRoutine;
        private Button       _btnResearch;

        private Unity.Entities.EntityQuery _tutorialQuery;
        private bool                        _queryReady;

        void Start()
        {
            if (uiDocument != null)
                _btnResearch = uiDocument.rootVisualElement.Q<Button>("btn-research");

            if (dialogueController != null)
            {
                dialogueController.OnDialogueComplete    += OnIntroComplete;
                dialogueController.OnHighlightRequested  += OnHighlightRequested;
                dialogueController.OnActionTriggered     += OnActionTriggered;
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

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world != null)
            {
                _tutorialQuery = world.EntityManager.CreateEntityQuery(
                    Unity.Entities.ComponentType.ReadWrite<TutorialStateData>());
                _queryReady = true;
            }
        }

        void OnDestroy()
        {
            if (dialogueController != null)
            {
                dialogueController.OnDialogueComplete   -= OnIntroComplete;
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

            if (state.CurrentStep == _lastStep) return;

            _lastStep = state.CurrentStep;
            OnStepChanged(state.CurrentStep);
        }

        private void OnStepChanged(TutorialStep step)
        {
            // Clear state from the previous step
            if (_pulseRoutine != null) { StopCoroutine(_pulseRoutine); _pulseRoutine = null; }
            SetResearchButtonHighlight(false);
            tutorialHighlighter?.ClearHighlight();
            hudController?.HideTutorialHint();

            switch (step)
            {
                case TutorialStep.IntroDialogue:
                    if (dialogueController != null && introDialogue != null)
                        dialogueController.PlayDialogue(introDialogue);
                    else
                        AdvanceECSStep(TutorialStep.CollectFirstElectron);
                    break;

                case TutorialStep.CollectFirstElectron:
                    tutorialHighlighter?.ShowVisualOnly("electron_field");
                    hudController?.ShowTutorialHint("Tap the electron field to collect 5 electrons.");
                    break;

                case TutorialStep.DirectToMaxwellsDemon:
                    tutorialHighlighter?.ShowHighlight("maxwells_demon");
                    hudController?.ShowTutorialHint("Tap Maxwell's Demon to sell your electrons.");
                    break;

                case TutorialStep.SellElectronsInDemon:
                    // Item highlight inside the panel is applied via OnDemonOpened.
                    hudController?.ShowTutorialHint("Drag your electrons to the right side to sell them.");
                    break;

                case TutorialStep.CloseDemonPanel:
                    maxwellsDemon?.ClearTutorialHighlight();
                    hudController?.ShowTutorialHint("Great! Close the panel when you're ready.");
                    break;

                case TutorialStep.BuyRecombinationI:
                    _pulseRoutine = StartCoroutine(PulseButton(_btnResearch));
                    hudController?.ShowTutorialHint("Open Research and buy Recombination I.");
                    break;

                case TutorialStep.CraftFirstQuarks:
                    hudController?.ShowTutorialHint("Place a Field Collector on a quark field to harvest quarks.");
                    break;

                case TutorialStep.CraftFirstProton:
                    hudController?.ShowTutorialHint("Place a Nucleon Factory and connect it to your Field Collector.");
                    break;
            }
        }

        private void OnIntroComplete()
        {
            // Move the ECS tutorial step forward after the intro dialogue finishes
            AdvanceECSStep(TutorialStep.CollectFirstElectron);
        }

        // ── ECS helper ───────────────────────────────────────────────────────

        private void AdvanceECSStep(TutorialStep next)
        {
            if (!_queryReady || _tutorialQuery.IsEmpty) return;

            var state = _tutorialQuery.GetSingleton<TutorialStateData>();
            state.CurrentStep = next;
            _tutorialQuery.SetSingleton(state);

            // Set _lastStep first so Update() won't fire OnStepChanged a second time,
            // then call it directly so the UI reacts this frame rather than next.
            _lastStep = next;
            OnStepChanged(next);
        }

        // ── UI helpers ───────────────────────────────────────────────────────

        private void SetResearchButtonHighlight(bool on)
        {
            if (_btnResearch == null) return;
            if (on) _btnResearch.AddToClassList("panel-btn--highlight");
            else    _btnResearch.RemoveFromClassList("panel-btn--highlight");
        }

        private IEnumerator PulseButton(Button btn)
        {
            if (btn == null) yield break;
            while (true)
            {
                btn.AddToClassList("panel-btn--highlight");
                yield return new WaitForSecondsRealtime(0.7f);
                btn.RemoveFromClassList("panel-btn--highlight");
                yield return new WaitForSecondsRealtime(0.7f);
            }
        }

        // ── Dialogue event handlers ──────────────────────────────────────────

        private void OnHighlightRequested(string target)
        {
            SetResearchButtonHighlight(false);

            if (string.IsNullOrEmpty(target))
            {
                // Empty target = clear all highlights and return camera to character
                tutorialHighlighter?.ClearHighlight();
                return;
            }

            switch (target)
            {
                case "btn-research":
                    SetResearchButtonHighlight(true);
                    break;
                default:
                    // World-space object (field, building on grid) — stub until grid system exists
                    HighlightWorldObject(target);
                    break;
            }
        }

        private void OnActionTriggered(string action)
        {
            if (string.IsNullOrEmpty(action)) return;
            // Stub: grid/camera actions implemented when the grid system is built
            Debug.Log($"[TutorialOverlay] Action: {action}");
        }

        private void HighlightWorldObject(string targetId)
        {
            tutorialHighlighter?.ShowHighlight(targetId);
        }

        // ── Maxwell's Demon event handlers ───────────────────────────────────

        private void OnDemonOpened()
        {
            if (!_queryReady || _tutorialQuery.IsEmpty) return;
            var state = _tutorialQuery.GetSingleton<TutorialStateData>();

            if (state.CurrentStep == TutorialStep.DirectToMaxwellsDemon)
            {
                tutorialHighlighter?.ClearHighlight();
                AdvanceECSStep(TutorialStep.SellElectronsInDemon);

                // Highlight the electron item row (itemId = 3) inside the panel
                maxwellsDemon?.HighlightTutorialItem(3);
            }
        }

        private void OnDemonItemsDeposited()
        {
            if (!_queryReady || _tutorialQuery.IsEmpty) return;
            var state = _tutorialQuery.GetSingleton<TutorialStateData>();

            // TutorialSystem advances SellElectronsInDemon → CloseDemonPanel automatically
            // once inventory hits 0, but we also clear the item highlight immediately.
            if (state.CurrentStep == TutorialStep.SellElectronsInDemon ||
                state.CurrentStep == TutorialStep.CloseDemonPanel)
            {
                maxwellsDemon?.ClearTutorialHighlight();
            }
        }

        private void OnDemonClosed()
        {
            if (!_queryReady || _tutorialQuery.IsEmpty) return;
            var state = _tutorialQuery.GetSingleton<TutorialStateData>();

            if (state.CurrentStep == TutorialStep.CloseDemonPanel)
                AdvanceECSStep(TutorialStep.BuyRecombinationI);
        }
    }
}
