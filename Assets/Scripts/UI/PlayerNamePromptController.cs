using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// "Choose your name" card (PvE phase 1): the public name shown on tournament leaderboards.
    ///  - Shown by itself once per launch after the tutorial, until the player sets a name or taps
    ///    <b>Later</b> (<see cref="SaveData.namePromptSkipped"/>). Only when online, since UGS stores the name.
    ///  - Opened from Settings &gt; Account to set or change it at any time.
    /// The name is checked as it is typed (<see cref="PlayerNameRules"/>) and saved by
    /// <see cref="PlayerNameService"/>. Added to the HUD GameObject by <see cref="HUDController"/>
    /// (GetComponent ?? AddComponent), so no scene wiring is needed.
    /// </summary>
    public class PlayerNamePromptController : MonoBehaviour
    {
        const float PollSeconds        = 1f;
        const float AutoPromptDelaySec = 3f;   // before the notification opt-in card (8 s), so they don't collide

        public const string AutoMessage =
            "Pick a name for the tournament leaderboards. Other players will see it.";

        HUDController _hud;
        VisualElement _card;
        Label         _title;
        Label         _message;
        Label         _error;
        TextField     _input;
        Button        _save;
        Button        _skip;
        readonly object _modalOwner = new();

        bool  _autoMode;          // opened by itself (Later = don't ask again) vs from Settings (Cancel)
        bool  _busy;
        bool  _handledThisLaunch;
        bool  _refreshStarted;
        bool  _refreshDone;
        float _nextPoll;
        float _readySince = -1f;

        /// <summary>True while the card is open (any instance).</summary>
        public static bool IsOpen { get; private set; }

        /// <summary>Raised after a name is saved, so Settings can refresh its row.</summary>
        public event Action OnSaved;

        public void Init(VisualElement root, HUDController hud)
        {
            _hud     = hud;
            _card    = root?.Q("name-prompt");
            if (_card == null)
            {
                GameLogger.Warning("[PlayerName] name-prompt missing from GameHUD.uxml; name card disabled.");
                return;
            }
            _title   = _card.Q<Label>("name-prompt__title");
            _message = _card.Q<Label>("name-prompt__message");
            _error   = _card.Q<Label>("name-prompt__error");
            _input   = _card.Q<TextField>("name-prompt__input");
            _save    = _card.Q<Button>("name-prompt__save");
            _skip    = _card.Q<Button>("name-prompt__skip");

            if (_input != null)
            {
                _input.maxLength = PlayerNameRules.MaxLength;
                _input.RegisterValueChangedCallback(_ => RefreshValidation());
            }
            if (_save != null) _save.clicked += Save;
            if (_skip != null) _skip.clicked += Dismiss;
            Close();
        }

        /// <summary>
        /// Whether the card should open by itself now. Pure so the rule is unit-tested.
        /// </summary>
        public static bool ShouldAutoPrompt(bool gameLoaded, bool tutorialActive, bool hasName, bool skipped,
                                            bool online, bool otherModalOpen, bool dialoguePlaying,
                                            bool reloadPending)
            => gameLoaded && !tutorialActive && !hasName && !skipped && online
               && !otherModalOpen && !dialoguePlaying && !reloadPending;

        /// <summary>Opens the card from Settings (Cancel closes it without remembering anything).</summary>
        public void OpenFromSettings() => Open(auto: false);

        /// <summary>Android back: same as Later / Cancel.</summary>
        public void CancelFromBack() { if (IsOpen && !_busy) Dismiss(); }

        // ── Auto prompt ───────────────────────────────────────────────────

        void Update()
        {
            if (_card == null || _handledThisLaunch || IsOpen) return;
            if (Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + PollSeconds;

            var save   = SaveManager.Instance?.Current;
            bool loaded = ECSLoadBridge.Instance != null && ECSLoadBridge.Instance.IsLoaded;
            bool tutorialActive = save?.tutorial == null || save.tutorial.isActive;
            if (save == null || !loaded || tutorialActive) { _readySince = -1f; return; }

            // Ask UGS first: a name chosen on another device (same account) means no card here.
            if (!_refreshStarted)
            {
                _refreshStarted = true;
                RefreshThenContinue();
                return;
            }
            if (!_refreshDone) return;

            if (PlayerNameService.HasName || save.namePromptSkipped) { _handledThisLaunch = true; return; }
            bool online = PlayerNameService.Backend != null && PlayerNameService.Backend.IsReady;
            if (!online) { _handledThisLaunch = true; return; }   // can't save offline: try next launch

            bool otherModal = ConfirmPrompt.IsOpen || SaveConflictPrompt.IsOpen;
            bool canShow = ShouldAutoPrompt(loaded, tutorialActive, PlayerNameService.HasName, save.namePromptSkipped,
                                            online, otherModal, IsDialoguePlaying(), PrestigeRunReset.ReloadPending);
            if (!canShow) { _readySince = -1f; return; }
            if (_readySince < 0f) _readySince = Time.unscaledTime;
            if (Time.unscaledTime - _readySince < AutoPromptDelaySec) return;

            _handledThisLaunch = true;
            Open(auto: true);
        }

        async void RefreshThenContinue()
        {
            try { await PlayerNameService.RefreshAsync(); }
            catch (Exception e) { GameLogger.Warning($"[PlayerName] Refresh failed: {e.Message}"); }
            _refreshDone = true;
        }

        // ── Card ──────────────────────────────────────────────────────────

        void Open(bool auto)
        {
            if (_card == null) return;
            _autoMode = auto;
            _busy     = false;
            if (_title != null) _title.text = PlayerNameService.HasName ? "Change your name" : "Choose your name";
            if (_message != null)
            {
                string current = PlayerNameService.Current;
                _message.text = string.IsNullOrEmpty(current)
                    ? AutoMessage
                    : $"Your name is {PlayerNameRules.WithoutTag(current)} {PlayerNameRules.Tag(current)}. Other players see it on the tournament leaderboards.";
            }
            if (_skip != null) _skip.text = auto ? "Later" : "Cancel";
            _input?.SetValueWithoutNotify(PlayerNameService.DisplayName);
            RefreshValidation();

            IsOpen = true;
            HUDController.SetElementVisible(_card, true);
            UIInputBlocker.SetModal(_modalOwner, true);
            _input?.Focus();
        }

        void Dismiss()
        {
            if (_busy) return;
            if (_autoMode)
            {
                var save = SaveManager.Instance?.Current;
                if (save != null && !save.namePromptSkipped)
                {
                    save.namePromptSkipped = true;
                    SaveManager.Instance.SaveLocal();
                }
            }
            Close();
        }

        void Close()
        {
            IsOpen = false;
            _busy  = false;
            HUDController.SetElementVisible(_card, false);
            UIInputBlocker.SetModal(_modalOwner, false);
        }

        void RefreshValidation()
        {
            if (_busy) return;
            string name  = PlayerNameRules.Clean(_input?.value);
            var    check = PlayerNameRules.Validate(name, PlayerNameService.Patterns);
            bool unchanged = name == PlayerNameService.DisplayName && PlayerNameService.HasName;
            if (_error != null) _error.text = check == NameCheck.Empty ? "" : PlayerNameRules.Message(check);
            _save?.SetEnabled(check == NameCheck.Ok && !unchanged);
        }

        async void Save()
        {
            if (_busy || _input == null) return;
            _busy = true;
            _save?.SetEnabled(false);
            _skip?.SetEnabled(false);
            if (_error != null) _error.text = "Saving...";

            NameSetResult result;
            try { result = await PlayerNameService.TrySetAsync(_input.value); }
            catch (Exception e)
            {
                GameLogger.Warning($"[PlayerName] Save threw: {e.Message}");
                result = new NameSetResult(NameSetStatus.Failed, NameCheck.Ok, null, "Couldn't save the name. Try again.");
            }

            if (this == null) return;   // HUD destroyed (scene reload) while waiting
            _busy = false;
            _skip?.SetEnabled(true);

            if (result.Ok)
            {
                Close();
                _hud?.ShowNotification("✓", $"Name saved: {PlayerNameRules.WithoutTag(result.Name)}");
                OnSaved?.Invoke();
                return;
            }
            RefreshValidation();
            if (_error != null) _error.text = result.Message;
        }

        static bool IsDialoguePlaying()
        {
            var dialogue = FindAnyObjectByType<DialogueController>();
            return dialogue != null && dialogue.IsPlaying;
        }

        void OnDestroy()
        {
            if (IsOpen) UIInputBlocker.SetModal(_modalOwner, false);
            IsOpen = false;
        }
    }
}
