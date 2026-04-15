using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Drives the bottom-bar dialogue box in GameHUD.
    /// Call PlayDialogue(DialogueSO) to begin a sequence; subscribe to OnDialogueComplete
    /// to be notified when the last line is dismissed.
    ///
    /// Attach to any scene GameObject; wire the UIDocument reference in the Inspector
    /// (or it will auto-find one in the scene on Start).
    /// </summary>
    public class DialogueController : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument;

        public event Action OnDialogueComplete;
        /// <summary>
        /// Fired each time a new line is shown. Passes the highlight_target string
        /// (empty string = clear any existing highlight).
        /// </summary>
        public event Action<string> OnHighlightRequested;
        /// <summary>
        /// Fired each time a new line is shown and that line has a non-empty action string.
        /// Routed to TutorialOverlayController for one-shot directives.
        /// </summary>
        public event Action<string> OnActionTriggered;

        private VisualElement _box;
        private VisualElement _blocker;
        private VisualElement _portrait;
        private Label         _speaker;
        private Label         _text;
        private Button        _nextBtn;

        private DialogueSO _current;
        private int        _lineIndex;

        void Start()
        {
            if (_uiDocument == null)
                _uiDocument = FindAnyObjectByType<UIDocument>();
            if (_uiDocument == null) return;

            var root = _uiDocument.rootVisualElement;
            _box     = root.Q("dialogue-box");
            _blocker = root.Q("dialogue-blocker");
            _portrait = root.Q("dialogue-portrait");
            _speaker = root.Q<Label>("dialogue-speaker");
            _text    = root.Q<Label>("dialogue-text");
            _nextBtn = root.Q<Button>("btn-dialogue-next");

            if (_nextBtn != null)
                _nextBtn.clicked += AdvanceLine;

            if (_box != null)
                _box.AddToClassList("hidden");

            if (_blocker != null)
                _blocker.AddToClassList("hidden");
        }

        void OnDisable()
        {
            if (_nextBtn != null)
                _nextBtn.clicked -= AdvanceLine;
        }

        /// <summary>Begins playing a dialogue sequence from the first line.</summary>
        public void PlayDialogue(DialogueSO dialogue)
        {
            if (dialogue == null || dialogue.lines == null || dialogue.lines.Length == 0)
            {
                OnDialogueComplete?.Invoke();
                return;
            }

            _current   = dialogue;
            _lineIndex = 0;
            ShowLine(_lineIndex);
            SetBoxVisible(true);
        }

        /// <summary>Hides the dialogue box immediately.</summary>
        public void HideDialogue()
        {
            SetBoxVisible(false);
            _current = null;
        }

        private void AdvanceLine()
        {
            _lineIndex++;
            if (_current == null || _lineIndex >= _current.lines.Length)
            {
                SetBoxVisible(false);
                _current = null;
                OnDialogueComplete?.Invoke();
                return;
            }
            ShowLine(_lineIndex);
        }

        private void ShowLine(int index)
        {
            var line = _current.lines[index];

            if (_speaker != null)
                _speaker.text = line.speakerName ?? string.Empty;

            if (_text != null)
                _text.text = line.text ?? string.Empty;

            if (_portrait != null)
            {
                if (line.portrait != null)
                {
                    _portrait.style.backgroundImage = new StyleBackground(line.portrait);
                    _portrait.style.display = DisplayStyle.Flex;
                }
                else
                {
                    _portrait.style.display = DisplayStyle.None;
                }
            }

            bool isLast = _current == null || _lineIndex >= _current.lines.Length - 1;
            if (_nextBtn != null)
                _nextBtn.text = isLast ? "✓" : "▶";

            // Pause / unpause game time based on line directive
            Time.timeScale = line.pauseGame ? 0f : 1f;

            // Broadcast highlight target (empty string = clear)
            OnHighlightRequested?.Invoke(line.highlightTarget ?? "");

            // Fire one-shot action if present
            if (!string.IsNullOrEmpty(line.action))
                OnActionTriggered?.Invoke(line.action);
        }

        private void SetBoxVisible(bool visible)
        {
            if (_box == null) return;
            if (visible)
            {
                _box.RemoveFromClassList("hidden");
                _blocker?.RemoveFromClassList("hidden");
            }
            else
            {
                _box.AddToClassList("hidden");
                _blocker?.AddToClassList("hidden");

                // Always restore time and clear highlights when dialogue closes
                Time.timeScale = 1f;
                OnHighlightRequested?.Invoke("");
            }
        }
    }
}
