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
    /// Two hosts carry the same markup (element names <c>save-choice-*</c>):
    ///  - the loading screen (<see cref="InitLoadingScreen"/>, LoadingScreen.uxml). At startup the choice is
    ///    made there, before the save is applied to ECS, so the chosen save is the only one ever loaded;
    ///    ECSLoadBridge waits for it (<see cref="IsShownOnLoadingScreen"/>).
    ///  - the HUD (<see cref="Init"/>, GameHUD.uxml), for a choice that comes up while the game runs (a
    ///    slow cloud fetch after the load, or Settings > Sign in with Google). Taking the cloud save then
    ///    reloads the scene (SaveManager.TakeCloudSave, live path).
    /// <see cref="IsReady"/> is true while either host can show the prompt; the loading screen wins.
    /// </summary>
    public static class SaveConflictPrompt
    {
        internal const string LocalTitle = "Keep this device's save";
        internal const string CloudTitle = "Use the cloud save";

        /// <summary>The prompt's elements inside one host.</summary>
        internal sealed class Host
        {
            public VisualElement HostRoot, Root, Options, ConfirmRow;
            public Label Message, LocalSummary, CloudSummary;
            public Button ConfirmButton;

            public static Host Bind(VisualElement hostRoot)
            {
                var root = hostRoot?.Q("save-choice-modal");
                if (root == null) return null;
                var h = new Host
                {
                    HostRoot     = hostRoot,
                    Root         = root,
                    Options      = root.Q("save-choice__options"),
                    ConfirmRow   = root.Q("save-choice__confirm-row"),
                    Message      = root.Q<Label>("save-choice__message"),
                    LocalSummary = root.Q<Label>("save-choice__local-summary"),
                    CloudSummary = root.Q<Label>("save-choice__cloud-summary"),
                    ConfirmButton = root.Q<Button>("save-choice__confirm"),
                };
                var local = root.Q<Button>("save-choice__local");
                var cloud = root.Q<Button>("save-choice__cloud");
                var back  = root.Q<Button>("save-choice__back");
                if (local     != null) local.clicked     += () => Pick(true);
                if (cloud     != null) cloud.clicked     += () => Pick(false);
                if (back      != null) back.clicked      += ShowOptions;
                if (h.ConfirmButton != null) h.ConfirmButton.clicked += Confirm;
                return h;
            }

            public bool Attached => Root != null && Root.panel != null;
        }

        static Host s_hud, s_loading, s_active;
        static Action<bool> s_onChoice;
        static bool s_pendingKeepLocal;
        static readonly object s_modalOwner = new();

        public static bool IsOpen { get; private set; }

        /// <summary>True while the prompt is open on the loading screen (the save is not applied yet).</summary>
        public static bool IsShownOnLoadingScreen => IsOpen && s_active != null && s_active == s_loading;

        /// <summary>The loading screen can host the prompt right now.</summary>
        public static bool LoadingScreenReady => s_loading != null && s_loading.Attached;

        static bool HudReady =>
            s_hud != null && s_hud.Attached && !SceneLoader.IsTransitioning &&
            s_hud.HostRoot.resolvedStyle.display != DisplayStyle.None;

        public static bool IsReady => LoadingScreenReady || HudReady;

        /// <summary>Called by HUDController with the HUD root.</summary>
        public static void Init(VisualElement hudRoot)
        {
            s_hud = Host.Bind(hudRoot);
            if (s_hud == null) return;
            HUDController.SetElementVisible(s_hud.Root, IsOpen && s_active == s_hud);
        }

        /// <summary>Called by LoadingScreenController with its root, so a startup choice is made before the load.</summary>
        public static void InitLoadingScreen(VisualElement loadingRoot)
        {
            s_loading = Host.Bind(loadingRoot);
            if (s_loading != null) HUDController.SetElementVisible(s_loading.Root, false);
        }

        /// <summary>The loading screen is going away.</summary>
        public static void ReleaseLoadingScreen()
        {
            if (s_loading != null && s_active == s_loading && IsOpen)
                GameLogger.Warning("[SaveConflictPrompt] Loading screen closed while the save choice was open on it.");
            s_loading = null;
        }

        /// <summary>Opens the choice. <paramref name="onChoice"/> gets true to keep this device's save.</summary>
        public static void Show(string localSummary, string cloudSummary, Action<bool> onChoice)
        {
            s_active = LoadingScreenReady ? s_loading : s_hud;
            if (s_active == null) { GameLogger.Error("[SaveConflictPrompt] Show called with no host."); return; }

            s_onChoice = onChoice;
            if (s_active.LocalSummary != null) s_active.LocalSummary.text = localSummary;
            if (s_active.CloudSummary != null) s_active.CloudSummary.text = cloudSummary;
            ShowOptions();
            IsOpen = true;
            HUDController.SetElementVisible(s_active.Root, true);
            UIInputBlocker.SetModal(s_modalOwner, true);
            GameLogger.Info($"[SaveConflictPrompt] Showing the save choice on the {(s_active == s_loading ? "loading screen" : "HUD")}.");
        }

        internal const string ChooseMessage =
            "This device and your cloud save have different progress. Choose the one to keep on all your devices.";

        internal static string ConfirmMessage(bool keepLocal) => keepLocal
            ? "Keep this device's save? It replaces the cloud save, so your other devices load it next time."
            : "Use the cloud save? It replaces this device's progress.";

        static void ShowOptions()
        {
            if (s_active == null) return;
            if (s_active.Message != null) s_active.Message.text = ChooseMessage;
            HUDController.SetElementVisible(s_active.Options, true);
            HUDController.SetElementVisible(s_active.ConfirmRow, false);
        }

        static void Pick(bool keepLocal)
        {
            if (s_active == null) return;
            s_pendingKeepLocal = keepLocal;
            if (s_active.Message != null) s_active.Message.text = ConfirmMessage(keepLocal);
            if (s_active.ConfirmButton != null) s_active.ConfirmButton.text = keepLocal ? "Keep this device's" : "Use cloud save";
            HUDController.SetElementVisible(s_active.Options, false);
            HUDController.SetElementVisible(s_active.ConfirmRow, true);
        }

        static void Confirm()
        {
            var callback = s_onChoice;
            s_onChoice = null;
            IsOpen = false;
            if (s_active != null) HUDController.SetElementVisible(s_active.Root, false);
            s_active = null;
            UIInputBlocker.SetModal(s_modalOwner, false);
            callback?.Invoke(s_pendingKeepLocal);
        }

        /// <summary>Test seam: forget both hosts and any open choice.</summary>
        internal static void ResetForTests()
        {
            s_hud = s_loading = s_active = null;
            s_onChoice = null;
            IsOpen = false;
        }
    }
}
