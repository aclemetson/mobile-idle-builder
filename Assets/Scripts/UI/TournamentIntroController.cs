using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Tournament onboarding (PvE phase 6). Once per entry window (Friday 00:00 to Saturday 00:00 UTC), when
    /// tournaments are unlocked and the player hasn't entered this week, it plays a dialogue about the tournament:
    /// the full explainer (<see cref="FullIntroId"/>) the first time, a short "it's open" line after that
    /// (<see cref="WeeklyIntroId"/>). It waits until the game has been calm for <see cref="CalmSeconds"/> (no
    /// tutorial, dialogue or modal), so finishing the Chemistry tutorial inside the window shows its last message,
    /// then about 5 s later this one. When the dialogue ends it pulses the way to the Tournament button (drawer
    /// handle, then the button) until the panel is opened, which shows the explainer and Start.
    /// Added to the HUD GameObject by <see cref="HUDController"/> (GetComponent ?? AddComponent): no scene wiring.
    /// </summary>
    public class TournamentIntroController : MonoBehaviour
    {
        public const string FullIntroId   = "pve_intro";
        public const string WeeklyIntroId = "pve_intro_weekly";
        public const float  CalmSeconds   = 5f;
        public const float  PulseSeconds  = 30f;
        const string TargetButton = "btn-tournament";
        const string PulseClass   = "panel-btn--highlight";

        VisualElement _root;
        TournamentPanelController _panel;
        DialogueController _dialogue;
        float _calmSince = -1f;
        float _nextCheck;
        bool  _awaitingDialogueEnd;
        float _pulseUntil = -1f;
        float _pulseToggleAt;
        VisualElement _pulsing;

        public void Init(VisualElement root, HUDController hud)
        {
            _root = root;
            _panel = GetComponent<TournamentPanelController>();
        }

        /// <summary>
        /// Whether the intro should play now. Pure so the rule is unit-tested. <paramref name="calm"/> = no
        /// tutorial, dialogue or modal in the way and the game is loaded.
        /// </summary>
        public static bool ShouldShow(bool enabled, SaveData save, System.DateTime nowUtc, bool calm)
        {
            if (!enabled || save == null || !calm) return false;
            if (!TournamentRules.IsUnlocked(save)) return false;
            if (PveRules.Phase(nowUtc) != PvePhase.Entry) return false;
            string week = PveRules.WeekId(nowUtc);
            if (save.tournamentIntroWeek == week) return false;          // once per entry window
            if (save.tournament?.week == week) return false;             // already entered this week
            return true;
        }

        /// <summary>The dialogue to play: the full explainer once, then the short weekly line.</summary>
        public static string DialogueFor(SaveData save) => save != null && save.tournamentIntroSeen ? WeeklyIntroId : FullIntroId;

        /// <summary>A tutorial is running: the main one, or the current world's own flow before it's done.</summary>
        public static bool AnyTutorialRunning(SaveData save, string currentFlowWorldId)
        {
            if (save == null) return true;
            if (save.tutorial != null && save.tutorial.isActive) return true;
            if (string.IsNullOrEmpty(currentFlowWorldId)) return false;
            return !(TutorialFlows.Slot(save, currentFlowWorldId, create: false)?.done ?? false);
        }

        void Update()
        {
            TickPulse();
            if (_awaitingDialogueEnd)
            {
                if (_dialogue == null || !_dialogue.IsPlaying)
                {
                    _awaitingDialogueEnd = false;
                    _pulseUntil = Time.unscaledTime + PulseSeconds;
                }
                return;
            }
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 0.5f;

            var save = SaveManager.Instance != null ? SaveManager.Instance.Current : null;
            _dialogue ??= FindAnyObjectByType<DialogueController>();
            bool loaded = ECSLoadBridge.Instance != null && ECSLoadBridge.Instance.IsLoaded && !PrestigeRunReset.ReloadPending;
            bool calm = loaded && save != null
                        && !AnyTutorialRunning(save, TutorialFlowSO.Current?.worldId)
                        && (_dialogue == null || !_dialogue.IsPlaying)
                        && !ConfirmPrompt.IsOpen && !SaveConflictPrompt.IsOpen && !PlayerNamePromptController.IsOpen
                        && (_panel == null || !_panel.IsOpen);

            if (!ShouldShow(TournamentService.Enabled, save, TournamentService.Now, calm)) { _calmSince = -1f; return; }
            if (_calmSince < 0f) _calmSince = Time.unscaledTime;
            if (Time.unscaledTime - _calmSince < CalmSeconds) return;
            Show(save);
        }

        void Show(SaveData save)
        {
            _calmSince = -1f;
            string id = DialogueFor(save);
            save.tournamentIntroWeek = PveRules.WeekId(TournamentService.Now);
            save.tournamentIntroSeen = true;
            SaveManager.Instance.SaveLocal();

            var dialogue = Resources.Load<DialogueDatabaseSO>("DialogueDatabase")?.Get(id);
            if (dialogue != null && _dialogue != null)
            {
                GameLogger.Info($"[PvE] Tournament intro: {id}");
                _dialogue.PlayDialogue(dialogue);
                _awaitingDialogueEnd = true;
            }
            else
            {
                GameLogger.Warning($"[PvE] Dialogue {id} missing; pointing at the Tournament button only.");
                _pulseUntil = Time.unscaledTime + PulseSeconds;
            }
        }

        // Same look and path as the tutorial's button pulse (TutorialPulseResolver: drawer handle, then the button),
        // stopped as soon as the Tournament panel opens.
        void TickPulse()
        {
            if (_pulseUntil < 0f) return;
            bool done = Time.unscaledTime >= _pulseUntil || (_panel != null && _panel.IsOpen) || _root == null;
            if (done)
            {
                _pulsing?.RemoveFromClassList(PulseClass);
                _pulsing = null;
                _pulseUntil = -1f;
                return;
            }
            if (Time.unscaledTime < _pulseToggleAt) return;
            _pulseToggleAt = Time.unscaledTime + 0.7f;
            if (_pulsing != null)
            {
                _pulsing.RemoveFromClassList(PulseClass);
                _pulsing = null;
                return;
            }
            _pulsing = TutorialPulseResolver.Resolve(_root, TargetButton);
            _pulsing?.AddToClassList(PulseClass);
        }

        /// <summary>Dev console: forget this week's intro (and optionally the first-time one) so it shows again.</summary>
        public static string DevReset(bool full)
        {
            var save = SaveManager.Instance?.Current;
            if (save == null) return "Error: no save.";
            save.tournamentIntroWeek = null;
            if (full) save.tournamentIntroSeen = false;
            SaveManager.Instance.SaveLocal();
            return full ? "Tournament intro reset (full explainer next)." : "This week's tournament message reset.";
        }
    }
}
