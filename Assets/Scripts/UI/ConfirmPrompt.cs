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
        static Button        s_cancel;
        static Toggle        s_checkbox;
        static bool          s_checkboxShown;
        static Action        s_onConfirm;
        static Action        s_onCancel;
        static readonly object s_modalOwner = new();

        public static bool IsOpen { get; private set; }

        /// <summary>True once the HUD has called <see cref="Init"/>; until then Show confirms immediately.</summary>
        public static bool IsReady => s_root != null;

        /// <summary>
        /// Whether the optional checkbox (see <c>checkboxLabel</c> on <see cref="Show"/>) was ticked when the
        /// player answered. Read it inside the confirm/cancel callback.
        /// </summary>
        public static bool CheckboxChecked { get; private set; }

        const string DestructiveClass = "cancel-placement-btn";   // red: remove / demolish
        const string NeutralClass     = "craft-btn";              // the normal action button

        public static void Init(VisualElement hudRoot)
        {
            s_root = hudRoot?.Q("confirm-prompt");
            if (s_root == null) return;
            s_title   = s_root.Q<Label>("confirm-prompt__title");
            s_message = s_root.Q<Label>("confirm-prompt__message");
            s_confirm = s_root.Q<Button>("confirm-prompt__confirm");
            s_cancel  = s_root.Q<Button>("confirm-prompt__cancel");
            s_checkbox = s_root.Q<Toggle>("confirm-prompt__checkbox");

            if (s_confirm != null) s_confirm.clicked += Confirm;
            if (s_cancel  != null) s_cancel.clicked  += Cancel;
            Close();
        }

        /// <summary>
        /// Asks the player to confirm. <paramref name="onConfirm"/> runs on the confirm button;
        /// <paramref name="onCancel"/> (optional) on cancel or when replaced by another prompt.
        /// <paramref name="destructive"/> styles the confirm button red (the default, for removals); pass false
        /// for a plain question such as the notification opt-in. <paramref name="checkboxLabel"/> (optional) shows
        /// an unticked checkbox under the message; the callbacks read it from <see cref="CheckboxChecked"/>.
        /// </summary>
        public static void Show(string title, string message, string confirmLabel, Action onConfirm,
                                Action onCancel = null, string cancelLabel = "Cancel", bool destructive = true,
                                string checkboxLabel = null)
        {
            if (s_root == null) { CheckboxChecked = false; onConfirm?.Invoke(); return; }

            if (IsOpen) Cancel(); // a newer question replaces an unanswered one
            if (s_checkbox != null)
            {
                bool show = !string.IsNullOrEmpty(checkboxLabel);
                s_checkbox.text = checkboxLabel ?? "";
                s_checkbox.SetValueWithoutNotify(false);
                s_checkboxShown = show;
                HUDController.SetElementVisible(s_checkbox, show);
            }
            s_onConfirm = onConfirm;
            s_onCancel  = onCancel;
            if (s_title   != null) s_title.text   = title;
            if (s_message != null) s_message.text = message;
            if (s_confirm != null)
            {
                s_confirm.text = confirmLabel;
                s_confirm.EnableInClassList(DestructiveClass, destructive);
                s_confirm.EnableInClassList(NeutralClass, !destructive);
            }
            if (s_cancel != null) s_cancel.text = cancelLabel;

            IsOpen = true;
            HUDController.SetElementVisible(s_root, true);
            UIInputBlocker.SetModal(s_modalOwner, true);
        }

        /// <summary>Closes without running either callback (e.g. the mode that asked was cancelled).</summary>
        public static void Dismiss() => Close();

        /// <summary>Back button: same as tapping Cancel (runs the cancel callback).</summary>
        public static void CancelFromBack() { if (IsOpen) Cancel(); }

        static void Confirm()
        {
            var action = s_onConfirm;
            CaptureCheckbox();
            Close();
            action?.Invoke();
        }

        static void Cancel()
        {
            var action = s_onCancel;
            CaptureCheckbox();
            Close();
            action?.Invoke();
        }

        static void CaptureCheckbox() =>
            CheckboxChecked = s_checkbox != null && s_checkboxShown
                              && s_checkbox.value;

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
