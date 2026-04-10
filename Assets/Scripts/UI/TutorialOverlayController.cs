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
        [SerializeField] private DialogueController dialogueController;
        [SerializeField] private DialogueSO         introDialogue;
        [SerializeField] private UIDocument         uiDocument;

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
                dialogueController.OnDialogueComplete += OnIntroComplete;

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
                dialogueController.OnDialogueComplete -= OnIntroComplete;
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
            // Stop any previous pulse
            if (_pulseRoutine != null) { StopCoroutine(_pulseRoutine); _pulseRoutine = null; }
            SetResearchButtonHighlight(false);

            switch (step)
            {
                case TutorialStep.IntroDialogue:
                    if (dialogueController != null && introDialogue != null)
                        dialogueController.PlayDialogue(introDialogue);
                    else
                        AdvanceECSStep(TutorialStep.BuyRecombinationI);
                    break;

                case TutorialStep.BuyRecombinationI:
                    _pulseRoutine = StartCoroutine(PulseButton(_btnResearch));
                    break;

                case TutorialStep.CraftFirstQuarks:
                    ShowHint("Place a Field Collector on a quark field to harvest quarks.");
                    break;

                case TutorialStep.CraftFirstProton:
                    ShowHint("Place a Nucleon Factory and connect it to your Field Collector.");
                    break;
            }
        }

        private void OnIntroComplete()
        {
            // Move the ECS tutorial step forward after the intro dialogue finishes
            AdvanceECSStep(TutorialStep.BuyRecombinationI);
        }

        // ── ECS helper ───────────────────────────────────────────────────────

        private void AdvanceECSStep(TutorialStep next)
        {
            if (!_queryReady || _tutorialQuery.IsEmpty) return;

            var state = _tutorialQuery.GetSingleton<TutorialStateData>();
            state.CurrentStep = next;
            _tutorialQuery.SetSingleton(state);

            _lastStep = next;
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
                yield return new WaitForSeconds(0.7f);
                btn.RemoveFromClassList("panel-btn--highlight");
                yield return new WaitForSeconds(0.7f);
            }
        }

        private void ShowHint(string message)
        {
            var hud = FindAnyObjectByType<HUDController>();
            hud?.ShowNotification("→", message, null);
        }
    }
}
