using System.IO;
using UnityEngine;

namespace MobileIdleBuilder
{
    [DefaultExecutionOrder(-80)]
    public class SettingsService : SingletonMonoBehaviour<SettingsService>
    {
        protected override bool PersistAcrossScenes => true;

        const string FileName = "settings.json";

        string       _filePath;
        SettingsData _current;

        public SettingsData Current => _current;

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;

            _filePath = Path.Combine(Application.persistentDataPath, FileName);
            _current  = Load() ?? new SettingsData();
            Apply();
        }

        // ── Public setters ────────────────────────────────────────────────

        public void SetMasterVolume(float v)         { _current.masterVolume        = v; Apply(); Save(); }
        public void SetSFXVolume(float v)            { _current.sfxVolume           = v; Apply(); Save(); }
        public void SetMusicVolume(float v)          { _current.musicVolume         = v; Apply(); Save(); }
        public void SetGraphicsQuality(int quality)  { _current.graphicsQuality     = quality; Apply(); Save(); }
        public void SetNotifications(bool enabled)   { _current.notificationsEnabled = enabled; Save(); }
        public void SetShowPowerConnections(bool on) { _current.showPowerConnections = on; Save(); }

        // ── Internal ──────────────────────────────────────────────────────

        void Apply()
        {
            AudioListener.volume = _current.masterVolume;
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
