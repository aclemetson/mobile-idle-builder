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
        }

        private void OnDisable() => Cleanup();

        // ── Public API ────────────────────────────────────────────────────────

        public void Open()
        {
            SyncFromSettings();
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
        }

        private void SyncFromSettings()
        {
            var s = SettingsService.Instance?.Current;
            if (s == null) return;

            if (_sliderMaster != null) _sliderMaster.SetValueWithoutNotify(s.masterVolume);
            if (_sliderSFX    != null) _sliderSFX.SetValueWithoutNotify(s.sfxVolume);
            if (_sliderMusic  != null) _sliderMusic.SetValueWithoutNotify(s.musicVolume);

            if (_toggleNotifications != null)
                _toggleNotifications.SetValueWithoutNotify(s.notificationsEnabled);

            if (_sliderPanSensitivity != null)
                _sliderPanSensitivity.SetValueWithoutNotify(s.panSensitivity);
            if (_toggleInvertTilt != null)
                _toggleInvertTilt.SetValueWithoutNotify(s.invertTilt);

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

        private void OnNotificationsChanged(ChangeEvent<bool> evt)
            => SettingsService.Instance?.SetNotifications(evt.newValue);

        private void OnPanSensitivityChanged(ChangeEvent<float> evt)
            => SettingsService.Instance?.SetPanSensitivity(evt.newValue);

        private void OnInvertTiltChanged(ChangeEvent<bool> evt)
            => SettingsService.Instance?.SetInvertTilt(evt.newValue);
    }
}
