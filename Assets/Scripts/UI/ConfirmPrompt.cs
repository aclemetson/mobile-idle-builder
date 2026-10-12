using System;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// A small centred Yes/No card on the HUD, used to confirm destructive actions such as removing
    /// a building or a belt tile. While it is open, world input is blocked (see
    /// <see cref="UIInputBlocker.SetModal"/>), so a tap on the scrim never reaches the grid.
    ///
    /// HUDController calls <see cref="Init"/> once with the HUD root; until then <see cref="Show"/>
    /// confirms straight away, so callers behave as before without a HUD (tests, other scenes).
    /// </summary>
    public static class ConfirmPrompt
    {
        static VisualElement s_root;
        static Label         s_title;
        static Label         s_message;
        static Button        s_confirm;
        static Action        s_onConfirm;
        static Action        s_onCancel;
        static readonly object s_modalOwner = new();

        public static bool IsOpen { get; private set; }

        public static void Init(VisualElement hudRoot)
        {
            s_root = hudRoot?.Q("confirm-prompt");
            if (s_root == null) return;
            s_title   = s_root.Q<Label>("confirm-prompt__title");
            s_message = s_root.Q<Label>("confirm-prompt__message");
            s_confirm = s_root.Q<Button>("confirm-prompt__confirm");
            var cancel = s_root.Q<Button>("confirm-prompt__cancel");

            if (s_confirm != null) s_confirm.clicked += Confirm;
            if (cancel    != null) cancel.clicked    += Cancel;
            Close();
        }

        /// <summary>
        /// Asks the player to confirm. <paramref name="onConfirm"/> runs on the confirm button;
        /// <paramref name="onCancel"/> (optional) on cancel or when replaced by another prompt.
        /// </summary>
        public static void Show(string title, string message, string confirmLabel, Action onConfirm,
                                Action onCancel = null)
        {
            if (s_root == null) { onConfirm?.Invoke(); return; }

            if (IsOpen) Cancel(); // a newer question replaces an unanswered one
            s_onConfirm = onConfirm;
            s_onCancel  = onCancel;
            if (s_title   != null) s_title.text   = title;
            if (s_message != null) s_message.text = message;
            if (s_confirm != null) s_confirm.text = confirmLabel;

            IsOpen = true;
            HUDController.SetElementVisible(s_root, true);
            UIInputBlocker.SetModal(s_modalOwner, true);
        }

        /// <summary>Closes without running either callback (e.g. the mode that asked was cancelled).</summary>
        public static void Dismiss() => Close();

        static void Confirm()
        {
            var action = s_onConfirm;
            Close();
            action?.Invoke();
        }

        static void Cancel()
        {
            var action = s_onCancel;
            Close();
            action?.Invoke();
        }

        static void Close()
        {
            IsOpen      = false;
            s_onConfirm = null;
            s_onCancel  = null;
            HUDController.SetElementVisible(s_root, false);
            UIInputBlocker.SetModal(s_modalOwner, false);
        }
    }
}
