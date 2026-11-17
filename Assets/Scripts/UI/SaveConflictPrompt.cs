using System;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// "Two saves found": shown when this device's save and the cloud save have both progressed
    /// separately (docs/agents/save-system.md, "Cross-device saves"). Two cards summarise each save; the
    /// player picks one, then confirms. No close button and no Android back: one of the saves must win.
    /// The replaced save is kept as a file on the device (SaveManager.KeepCopy).
    ///
    /// HUDController calls <see cref="Init"/> with the HUD root. <see cref="IsReady"/> stays false until
    /// then and while the HUD is hidden for a scene transition; SaveManager waits for it.
    /// </summary>
    public static class SaveConflictPrompt
    {
        internal const string LocalTitle = "Keep this device's save";
        internal const string CloudTitle = "Use the cloud save";

        static VisualElement s_hudRoot, s_root, s_options, s_confirmRow;
        static Label s_message, s_localSummary, s_cloudSummary;
        static Button s_confirm;
        static Action<bool> s_onChoice;
        static bool s_pendingKeepLocal;
        static readonly object s_modalOwner = new();

        public static bool IsOpen { get; private set; }

        public static bool IsReady =>
            s_root != null && s_root.panel != null && !SceneLoader.IsTransitioning &&
            s_hudRoot != null && s_hudRoot.resolvedStyle.display != DisplayStyle.None;

        public static void Init(VisualElement hudRoot)
        {
            s_hudRoot = hudRoot;
            s_root    = hudRoot?.Q("save-choice-modal");
            if (s_root == null) return;

            s_options      = s_root.Q("save-choice__options");
            s_confirmRow   = s_root.Q("save-choice__confirm-row");
            s_message      = s_root.Q<Label>("save-choice__message");
            s_localSummary = s_root.Q<Label>("save-choice__local-summary");
            s_cloudSummary = s_root.Q<Label>("save-choice__cloud-summary");
            s_confirm      = s_root.Q<Button>("save-choice__confirm");

            var local = s_root.Q<Button>("save-choice__local");
            var cloud = s_root.Q<Button>("save-choice__cloud");
            var back  = s_root.Q<Button>("save-choice__back");
            if (local     != null) local.clicked     += () => Pick(true);
            if (cloud     != null) cloud.clicked     += () => Pick(false);
            if (back      != null) back.clicked      += ShowOptions;
            if (s_confirm != null) s_confirm.clicked += Confirm;

            HUDController.SetElementVisible(s_root, IsOpen);
        }

        /// <summary>Opens the choice. <paramref name="onChoice"/> gets true to keep this device's save.</summary>
        public static void Show(string localSummary, string cloudSummary, Action<bool> onChoice)
        {
            s_onChoice = onChoice;
            if (s_localSummary != null) s_localSummary.text = localSummary;
            if (s_cloudSummary != null) s_cloudSummary.text = cloudSummary;
            ShowOptions();
            IsOpen = true;
            HUDController.SetElementVisible(s_root, true);
            UIInputBlocker.SetModal(s_modalOwner, true);
        }

        internal const string ChooseMessage =
            "This device and your cloud save have different progress. Choose the one to keep on all your devices.";

        internal static string ConfirmMessage(bool keepLocal) => keepLocal
            ? "Keep this device's save? It replaces the cloud save, so your other devices load it next time."
            : "Use the cloud save? It replaces this device's progress.";

        static void ShowOptions()
        {
            if (s_message != null) s_message.text = ChooseMessage;
            HUDController.SetElementVisible(s_options, true);
            HUDController.SetElementVisible(s_confirmRow, false);
        }

        static void Pick(bool keepLocal)
        {
            s_pendingKeepLocal = keepLocal;
            if (s_message != null) s_message.text = ConfirmMessage(keepLocal);
            if (s_confirm != null) s_confirm.text = keepLocal ? "Keep this device's" : "Use cloud save";
            HUDController.SetElementVisible(s_options, false);
            HUDController.SetElementVisible(s_confirmRow, true);
        }

        static void Confirm()
        {
            var callback = s_onChoice;
            s_onChoice = null;
            IsOpen = false;
            HUDController.SetElementVisible(s_root, false);
            UIInputBlocker.SetModal(s_modalOwner, false);
            callback?.Invoke(s_pendingKeepLocal);
        }
    }
}
