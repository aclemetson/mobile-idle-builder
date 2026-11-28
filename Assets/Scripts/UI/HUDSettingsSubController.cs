using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Manages the settings slide-panel in the game HUD.
    /// Reads initial values from SettingsService on Open() and writes back on change.
    /// Wire-up: attach as a sibling MonoBehaviour to HUDController on the same GameObject.
    /// </summary>
    [RequireComponent(typeof(HUDController))]
    public class HUDSettingsSubController : MonoBehaviour
    {
        private VisualElement _panel;

        private Slider _sliderMaster;
        private Slider _sliderSFX;
        private Slider _sliderMusic;

        private Button _btnQualityLow;
        private Button _btnQualityMedium;
        private Button _btnQualityHigh;

        private Toggle _toggleNotifications;

        private Slider _sliderPanSensitivity;
        private Toggle _toggleInvertTilt;
        private Toggle _toggleProgressBars;

        private Button _btnSendFeedback;
        private Button _btnPrivacyPolicy;
        private Button _btnDiscord;
        private Label  _versionLabel;

        private Label  _accountLabel;
        private Button _btnGoogleSignIn;
        private Button _btnPlayerName;
        private Button _btnDeleteAccount;
        private bool   _deleting;
        private bool   _signingIn;
        private string _accountStatus;   // result of the last Settings sign-in, shown under the account

        private Button _btnClose;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        public void Initialize(VisualElement root)
        {
            Cleanup();
            QueryElements(root);
            BindControls();
            HUDController.SetElementVisible(_panel, false);
        }

        public void Cleanup()
        {
            if (_btnClose            != null) _btnClose.clicked                -= Close;
            if (_sliderMaster        != null) _sliderMaster.UnregisterValueChangedCallback(OnMasterVolumeChanged);
            if (_sliderSFX           != null) _sliderSFX.UnregisterValueChangedCallback(OnSFXVolumeChanged);
            if (_sliderMusic         != null) _sliderMusic.UnregisterValueChangedCallback(OnMusicVolumeChanged);
            if (_btnQualityLow       != null) _btnQualityLow.clicked           -= OnQualityLow;
            if (_btnQualityMedium    != null) _btnQualityMedium.clicked        -= OnQualityMedium;
            if (_btnQualityHigh      != null) _btnQualityHigh.clicked          -= OnQualityHigh;
            if (_toggleNotifications != null) _toggleNotifications.UnregisterValueChangedCallback(OnNotificationsChanged);
            if (_sliderPanSensitivity != null) _sliderPanSensitivity.UnregisterValueChangedCallback(OnPanSensitivityChanged);
            if (_toggleInvertTilt    != null) _toggleInvertTilt.UnregisterValueChangedCallback(OnInvertTiltChanged);
            if (_toggleProgressBars  != null) _toggleProgressBars.UnregisterValueChangedCallback(OnProgressBarsChanged);
            if (_btnSendFeedback     != null) _btnSendFeedback.clicked         -= OnSendFeedback;
            if (_btnPrivacyPolicy    != null) _btnPrivacyPolicy.clicked        -= OnPrivacyPolicy;
            if (_btnDiscord          != null) _btnDiscord.clicked              -= OnDiscord;
            if (_btnGoogleSignIn     != null) _btnGoogleSignIn.clicked         -= OnGoogleSignIn;
            if (_btnPlayerName       != null) _btnPlayerName.clicked           -= OnPlayerName;
            if (_btnDeleteAccount    != null) _btnDeleteAccount.clicked        -= OnDeleteAccount;
            PlayerNameService.OnNameChanged -= OnNameChanged;
        }

        private void OnDisable() => Cleanup();

        // ── Public API ────────────────────────────────────────────────────────

        public void Open()
        {
            SyncFromSettings();
            RefreshAccount();
            // Remote Config may have published the invite since the last open.
            _btnDiscord?.EnableInClassList("hidden", !IsDiscordInvite(FeatureFlags.DiscordUrl));
            HUDController.SetElementVisible(_panel, true);
        }

        public void Close()
            => HUDController.SetElementVisible(_panel, false);

        // ── Internal ──────────────────────────────────────────────────────────

        private void QueryElements(VisualElement root)
        {
            _panel                = root.Q("settings-panel");
            _btnClose             = root.Q<Button>("btn-close-settings");
            _sliderMaster         = root.Q<Slider>("slider-master-volume");
            _sliderSFX            = root.Q<Slider>("slider-sfx-volume");
            _sliderMusic          = root.Q<Slider>("slider-music-volume");
            _btnQualityLow        = root.Q<Button>("btn-quality-low");
            _btnQualityMedium     = root.Q<Button>("btn-quality-medium");
            _btnQualityHigh       = root.Q<Button>("btn-quality-high");
            _toggleNotifications  = root.Q<Toggle>("toggle-notifications");
            _sliderPanSensitivity = root.Q<Slider>("slider-pan-sensitivity");
            _toggleInvertTilt     = root.Q<Toggle>("toggle-invert-tilt");
            _toggleProgressBars   = root.Q<Toggle>("toggle-progress-bars");
            _btnSendFeedback      = root.Q<Button>("btn-send-feedback");
            _btnPrivacyPolicy     = root.Q<Button>("btn-privacy-policy");
            _btnDiscord           = root.Q<Button>("btn-discord");
            _versionLabel         = root.Q<Label>("settings-version");
            _accountLabel         = root.Q<Label>("settings-account");
            _btnGoogleSignIn      = root.Q<Button>("btn-google-sign-in");
            _btnPlayerName        = root.Q<Button>("btn-player-name");
            _btnDeleteAccount     = root.Q<Button>("btn-delete-account");

            if (_versionLabel != null) _versionLabel.text = VersionLabel.Format();
        }

        private void BindControls()
        {
            if (_btnClose            != null) _btnClose.clicked                += Close;
            if (_sliderMaster        != null) _sliderMaster.RegisterValueChangedCallback(OnMasterVolumeChanged);
            if (_sliderSFX           != null) _sliderSFX.RegisterValueChangedCallback(OnSFXVolumeChanged);
            if (_sliderMusic         != null) _sliderMusic.RegisterValueChangedCallback(OnMusicVolumeChanged);
            if (_btnQualityLow       != null) _btnQualityLow.clicked           += OnQualityLow;
            if (_btnQualityMedium    != null) _btnQualityMedium.clicked        += OnQualityMedium;
            if (_btnQualityHigh      != null) _btnQualityHigh.clicked          += OnQualityHigh;
            if (_toggleNotifications != null) _toggleNotifications.RegisterValueChangedCallback(OnNotificationsChanged);
            if (_sliderPanSensitivity != null) _sliderPanSensitivity.RegisterValueChangedCallback(OnPanSensitivityChanged);
            if (_toggleInvertTilt    != null) _toggleInvertTilt.RegisterValueChangedCallback(OnInvertTiltChanged);
            if (_toggleProgressBars  != null) _toggleProgressBars.RegisterValueChangedCallback(OnProgressBarsChanged);
            if (_btnSendFeedback     != null) _btnSendFeedback.clicked         += OnSendFeedback;
            if (_btnPrivacyPolicy    != null) _btnPrivacyPolicy.clicked        += OnPrivacyPolicy;
            if (_btnDiscord          != null) _btnDiscord.clicked              += OnDiscord;
            if (_btnGoogleSignIn     != null) _btnGoogleSignIn.clicked         += OnGoogleSignIn;
            if (_btnPlayerName       != null) _btnPlayerName.clicked           += OnPlayerName;
            if (_btnDeleteAccount    != null) _btnDeleteAccount.clicked        += OnDeleteAccount;
            PlayerNameService.OnNameChanged += OnNameChanged;
        }

        private void SyncFromSettings()
        {
            var s = SettingsService.Instance?.Current;
            if (s == null) return;

            if (_sliderMaster != null) _sliderMaster.SetValueWithoutNotify(s.masterVolume);
            if (_sliderSFX    != null) _sliderSFX.SetValueWithoutNotify(s.sfxVolume);
            if (_sliderMusic  != null) _sliderMusic.SetValueWithoutNotify(s.musicVolume);

            if (_toggleNotifications != null)
                _toggleNotifications.SetValueWithoutNotify(NotificationService.Instance != null
                    ? NotificationService.Instance.IsActive
                    : s.notificationsEnabled);

            if (_sliderPanSensitivity != null)
                _sliderPanSensitivity.SetValueWithoutNotify(s.panSensitivity);
            if (_toggleInvertTilt != null)
                _toggleInvertTilt.SetValueWithoutNotify(s.invertTilt);
            if (_toggleProgressBars != null)
                _toggleProgressBars.SetValueWithoutNotify(s.showProgressBars);

            ApplyQualityButtonStyles(s.graphicsQuality);
        }

        private void OnMasterVolumeChanged(ChangeEvent<float> evt)
            => SettingsService.Instance?.SetMasterVolume(evt.newValue);

        private void OnSFXVolumeChanged(ChangeEvent<float> evt)
            => SettingsService.Instance?.SetSFXVolume(evt.newValue);

        private void OnMusicVolumeChanged(ChangeEvent<float> evt)
            => SettingsService.Instance?.SetMusicVolume(evt.newValue);

        private void OnQualityLow()    => SetQuality(0);
        private void OnQualityMedium() => SetQuality(1);
        private void OnQualityHigh()   => SetQuality(2);

        private void SetQuality(int level)
        {
            SettingsService.Instance?.SetGraphicsQuality(level);
            ApplyQualityButtonStyles(level);
        }

        private void ApplyQualityButtonStyles(int level)
        {
            SetQualityActive(_btnQualityLow,    level == 0);
            SetQualityActive(_btnQualityMedium, level == 1);
            SetQualityActive(_btnQualityHigh,   level == 2);
        }

        private static void SetQualityActive(Button btn, bool active)
        {
            if (btn == null) return;
            if (active) btn.AddToClassList("quality-btn--active");
            else        btn.RemoveFromClassList("quality-btn--active");
        }

        // Turning notifications on may need the OS permission: NotificationService asks for it, or opens the
        // OS settings page when the player denied it before. The toggle shows what will actually happen.
        private void OnNotificationsChanged(ChangeEvent<bool> evt)
        {
            var service = NotificationService.Instance;
            if (service == null) { SettingsService.Instance?.SetNotifications(evt.newValue); return; }
            service.SetEnabledFromSettings(evt.newValue, active =>
            {
                if (_toggleNotifications != null) _toggleNotifications.SetValueWithoutNotify(active);
            });
        }

        private void OnPanSensitivityChanged(ChangeEvent<float> evt)
            => SettingsService.Instance?.SetPanSensitivity(evt.newValue);

        private void OnInvertTiltChanged(ChangeEvent<bool> evt)
            => SettingsService.Instance?.SetInvertTilt(evt.newValue);

        private void OnProgressBarsChanged(ChangeEvent<bool> evt)
            => SettingsService.Instance?.SetShowProgressBars(evt.newValue);

        // ── Account (cross-device saves) ──────────────────────────────────────

        private void RefreshAccount()
        {
            var cloud = SaveManager.Instance?.Cloud;
            var kind  = cloud?.AccountKind ?? CloudAccountKind.None;
            if (_accountLabel != null)
                _accountLabel.text = AccountText(kind, cloud?.AccountEmail, cloud?.AccountPlayerId,
                                                 kind == CloudAccountKind.Guest ? cloud?.LastSignInError : null,
                                                 _accountStatus);
            if (_btnGoogleSignIn != null)
            {
                HUDController.SetElementVisible(_btnGoogleSignIn, kind == CloudAccountKind.Guest);
                _btnGoogleSignIn.SetEnabled(!_signingIn);
                _btnGoogleSignIn.text = _signingIn ? "Signing in..." : "Sign in with Google";
            }
            if (_btnPlayerName != null) _btnPlayerName.text = PlayerNameButtonText(PlayerNameService.Current);
            if (_btnDeleteAccount != null)
            {
                _btnDeleteAccount.SetEnabled(!_deleting && !_signingIn);
                _btnDeleteAccount.text = _deleting ? "Deleting account..." : "Delete account";
            }
        }

        // ── Delete account (store requirement) ───────────────────────────────

        internal const string DeleteAccountMessage =
            "This permanently deletes your progress, crystals, league standings and player name, on every device " +
            "signed in to this account. It can't be undone.\n\nStore purchases are not refunded. Ad-free can be " +
            "restored from the Shop afterwards.";

        internal const string DeleteAccountCheckbox = "I understand this can't be undone";

        private void OnDeleteAccount()
        {
            if (_deleting) return;
            ConfirmPrompt.Show("Delete account?", DeleteAccountMessage, "Delete forever",
                onConfirm: () =>
                {
                    if (!ConfirmPrompt.CheckboxChecked)
                    {
                        _accountStatus = "Account not deleted: tick the box to confirm.";
                        RefreshAccount();
                        return;
                    }
                    RunAccountDeletion();
                },
                checkboxLabel: DeleteAccountCheckbox);
        }

        private async void RunAccountDeletion()
        {
            _deleting = true;
            _accountStatus = "Deleting your account...";
            RefreshAccount();
            try
            {
                var (failure, error) = await AccountDeletion.RunAsync(new UgsAccountDeletionBackend());
                if (failure == AccountDeletionFailure.None)
                {
                    _accountStatus = null;
                    await AccountDeletion.ResetLocalGameAsync();   // reloads the scene as a new game
                    return;
                }
                _accountStatus = AccountDeletion.FailureMessage(failure, error);
            }
            catch (Exception e)
            {
                GameLogger.Error($"[Settings] Account deletion failed: {e}");
                _accountStatus = AccountDeletion.FailureMessage(AccountDeletionFailure.ServerData, e.Message);
            }
            _deleting = false;
            if (this != null) RefreshAccount();
        }

        /// <summary>The Settings name row: the current public name (with its UGS tag), or an invitation to pick one.</summary>
        internal static string PlayerNameButtonText(string fullName) =>
            string.IsNullOrEmpty(fullName)
                ? "Choose a player name"
                : $"Player name: {PlayerNameRules.WithoutTag(fullName)} {PlayerNameRules.Tag(fullName)}".TrimEnd();

        private void OnPlayerName() => GetComponent<PlayerNamePromptController>()?.OpenFromSettings();

        private void OnNameChanged(string _) { if (this != null) RefreshAccount(); }

        /// <summary>
        /// The account row: who the save belongs to and a short player id, so two devices can be checked
        /// against each other (same id = same save).
        /// </summary>
        internal static string AccountText(CloudAccountKind kind, string email, string playerId,
                                           string signInError, string status)
        {
            string text = kind switch
            {
                CloudAccountKind.Google => string.IsNullOrEmpty(email) ? "Signed in with Google" : $"Google: {email}",
                CloudAccountKind.Guest  => "Guest: progress is saved for this device only. Sign in with Google to play it on your other devices.",
                CloudAccountKind.Other  => "Signed in (test account)",
                _                       => "Offline: progress is saved on this device.",
            };
            if (kind != CloudAccountKind.None && !string.IsNullOrEmpty(playerId))
                text += $"\nPlayer ID {ShortId(playerId)}";
            if (!string.IsNullOrEmpty(status))
                text += $"\n{status}";
            else if (!string.IsNullOrEmpty(signInError))
                text += $"\nLast Google sign-in failed: {signInError}";
            return text;
        }

        internal static string ShortId(string playerId) =>
            playerId.Length <= 8 ? playerId : playerId.Substring(0, 8);

        private void OnGoogleSignIn()
        {
            if (_signingIn || SaveManager.Instance == null) return;
            _signingIn = true;
            _accountStatus = null;
            RefreshAccount();
            SaveManager.Instance.StartCoroutine(SaveManager.Instance.SignInWithGoogleFromSettings(msg =>
            {
                _signingIn = false;
                _accountStatus = msg;
                if (this != null) RefreshAccount();
            }));
        }

        // ── Support links ─────────────────────────────────────────────────────

        private void OnSendFeedback()
        {
            string playerId = SaveManager.Instance?.Current?.playerId;
            string device   = $"{SystemInfo.deviceModel} / {SystemInfo.operatingSystem}";
            Application.OpenURL(BuildFeedbackMailto(FeatureFlags.FeedbackEmail, Application.version, playerId, device));
            TelemetryService.Instance?.RecordFeedbackOpened("settings_email");
        }

        private void OnPrivacyPolicy() => Application.OpenURL(FeatureFlags.PrivacyPolicyUrl);

        private void OnDiscord()
        {
            string url = FeatureFlags.DiscordUrl;
            if (!IsDiscordInvite(url)) return;
            Application.OpenURL(url.Trim());
            TelemetryService.Instance?.RecordFeedbackOpened("settings_discord");
        }

        /// <summary>
        /// True for an https Discord invite (discord.gg/... or discord.com/invite/...). Anything else, including
        /// a blank or mistyped Remote Config value, keeps the Settings button hidden.
        /// </summary>
        public static bool IsDiscordInvite(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return false;
            string host = uri.Host.ToLowerInvariant();
            string path = uri.AbsolutePath.Trim('/');
            if (host == "discord.gg") return path.Length > 0;
            if (host == "discord.com" || host == "www.discord.com")
                return path.StartsWith("invite/", StringComparison.OrdinalIgnoreCase) && path.Length > "invite/".Length;
            return false;
        }

        /// <summary>
        /// Builds the Send Feedback <c>mailto:</c> link. The body is prefilled with the build version,
        /// player id and device so a tester's report can be traced to their save and analytics.
        /// </summary>
        public static string BuildFeedbackMailto(string email, string version, string playerId, string device)
        {
            string subject = $"Feedback {version}";
            string body    = "Describe the issue or idea:\n\n\n" +
                             "----\n" +
                             $"Version: {version}\n" +
                             $"Player ID: {(string.IsNullOrEmpty(playerId) ? "unknown" : playerId)}\n" +
                             $"Device: {device}\n";
            return $"mailto:{email}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";
        }
    }
}
