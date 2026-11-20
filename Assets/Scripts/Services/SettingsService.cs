using System;
using System.IO;
using UnityEngine;

namespace MobileIdleBuilder
{
    [DefaultExecutionOrder(-80)]
    public class SettingsService : SingletonMonoBehaviour<SettingsService>
    {
        protected override bool PersistAcrossScenes => true;

        internal const string FileName = "settings.json";

        string       _filePath;
        SettingsData _current;

        public SettingsData Current => _current;

        // Not placed in any scene: without this the service never existed in builds, so every setting
        // (volumes, notifications toggle, ...) was ignored and NotificationService/RemotePushService
        // read "toggle off". BeforeSceneLoad so it is ready before scene objects ask for it.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            new GameObject(nameof(SettingsService)).AddComponent<SettingsService>();
        }

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;

            _filePath = Path.Combine(LocalSaveService.SaveDirectory, FileName);
            _current  = Load() ?? new SettingsData();
            Apply();
        }

        // ── Public setters ────────────────────────────────────────────────

        public void SetMasterVolume(float v)         { _current.masterVolume        = v; Apply(); Save(); }
        public void SetSFXVolume(float v)            { _current.sfxVolume           = v; Apply(); Save(); }
        public void SetMusicVolume(float v)          { _current.musicVolume         = v; Apply(); Save(); }
        public void SetGraphicsQuality(int quality)  { _current.graphicsQuality     = quality; Apply(); Save(); }
        public void SetNotifications(bool enabled)   { _current.notificationsEnabled = enabled; Save(); }
        public void MarkNotificationPromptShown()    => SetNotificationPromptShown(true);
        public void SetNotificationPromptShown(bool shown) { _current.notificationPromptShown = shown; Save(); }

        /// <summary>Records an answer to the notification opt-in card (see <see cref="NotificationAskPolicy"/>).</summary>
        public void RecordNotificationAsk(DateTime nowUtc, bool neverAskAgain)
        {
            NotificationAskPolicy.RecordAsked(_current, nowUtc, neverAskAgain);
            Save();
        }

        /// <summary>Dev console: forget every answer so the card is shown again.</summary>
        public void ResetNotificationAsk()
        {
            NotificationAskPolicy.Reset(_current);
            Save();
        }
        public void SetShowPowerConnections(bool on) { _current.showPowerConnections = on; Save(); }
        public void SetPanSensitivity(float v)       { _current.panSensitivity = v; Save(); }
        public void SetInvertTilt(bool on)           { _current.invertTilt = on; Save(); }

        /// <summary>
        /// Drops every setting back to its default and applies it. This service survives scene loads,
        /// so a save wipe must reset it explicitly — otherwise the old preferences stay in memory and
        /// the next setter writes settings.json straight back out.
        /// </summary>
        public void ResetToDefaults()
        {
            _current = new SettingsData();
            Apply();
        }

        // ── Internal ──────────────────────────────────────────────────────

        void Apply()
        {
            AudioListener.volume = _current.masterVolume;
            // The project has fewer quality levels than the 0-2 the settings model assumes (default 2 is
            // out of range): leave the platform default alone rather than forcing another level.
            if (_current.graphicsQuality >= 0 && _current.graphicsQuality < QualitySettings.names.Length)
                QualitySettings.SetQualityLevel(_current.graphicsQuality, applyExpensiveChanges: false);
        }

        void Save()
        {
            File.WriteAllText(_filePath, JsonUtility.ToJson(_current, prettyPrint: false));
        }

        SettingsData Load()
        {
            if (!File.Exists(_filePath)) return null;
            return JsonUtility.FromJson<SettingsData>(File.ReadAllText(_filePath));
        }
    }
}
