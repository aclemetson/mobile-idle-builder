using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.PlayModeTests
{
    /// <summary>
    /// EditMode tests for SettingsService (MonoBehaviour lifecycle, mutation persistence).
    ///
    /// Backs up and restores settings.json around each test so developer preferences
    /// are never overwritten.
    /// </summary>
    public class SettingsServiceTests
    {
        string _settingsPath;
        string _backup;

        GameObject _go;

        [SetUp]
        public void SetUp()
        {
            _settingsPath = Path.Combine(Application.persistentDataPath, "settings.json");
            _backup       = File.Exists(_settingsPath) ? File.ReadAllText(_settingsPath) : null;
            if (File.Exists(_settingsPath)) File.Delete(_settingsPath);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
            {
                Object.DestroyImmediate(_go);
                _go = null;
            }

            if (File.Exists(_settingsPath)) File.Delete(_settingsPath);
            if (_backup != null) File.WriteAllText(_settingsPath, _backup);
        }

        // ── Defaults ─────────────────────────────────────────────────────────

        [Test]
        public void DefaultSettings_UsedOnFirstRun()
        {
            _go = new GameObject("SettingsService");
            { var svc = _go.AddComponent<SettingsService>(); RunAwake(svc); }

            var s = SettingsService.Instance.Current;
            Assert.AreEqual(1f,   s.masterVolume,         "Default masterVolume should be 1");
            Assert.AreEqual(1f,   s.sfxVolume,            "Default sfxVolume should be 1");
            Assert.AreEqual(1f,   s.musicVolume,          "Default musicVolume should be 1");
            Assert.AreEqual(2,    s.graphicsQuality,      "Default graphicsQuality should be 2 (High)");
            Assert.IsTrue(s.notificationsEnabled,         "Default notificationsEnabled should be true");
        }

        // ── Persistence ───────────────────────────────────────────────────────

        [Test]
        public void SetMasterVolume_PersistsAcrossRecreation()
        {
            _go = new GameObject("SettingsService");
            { var svc = _go.AddComponent<SettingsService>(); RunAwake(svc); }

            SettingsService.Instance.SetMasterVolume(0.4f);
            Object.DestroyImmediate(_go);
            _go = null;

            _go = new GameObject("SettingsService");
            { var svc = _go.AddComponent<SettingsService>(); RunAwake(svc); }

            Assert.AreEqual(0.4f, SettingsService.Instance.Current.masterVolume, 0.001f);
        }

        [Test]
        public void SetSFXVolume_PersistsAcrossRecreation()
        {
            _go = new GameObject("SettingsService");
            { var svc = _go.AddComponent<SettingsService>(); RunAwake(svc); }

            SettingsService.Instance.SetSFXVolume(0.25f);
            Object.DestroyImmediate(_go);
            _go = null;

            _go = new GameObject("SettingsService");
            { var svc = _go.AddComponent<SettingsService>(); RunAwake(svc); }

            Assert.AreEqual(0.25f, SettingsService.Instance.Current.sfxVolume, 0.001f);
        }

        [Test]
        public void SetGraphicsQuality_PersistsAcrossRecreation()
        {
            _go = new GameObject("SettingsService");
            { var svc = _go.AddComponent<SettingsService>(); RunAwake(svc); }

            SettingsService.Instance.SetGraphicsQuality(0);
            Object.DestroyImmediate(_go);
            _go = null;

            _go = new GameObject("SettingsService");
            { var svc = _go.AddComponent<SettingsService>(); RunAwake(svc); }

            Assert.AreEqual(0, SettingsService.Instance.Current.graphicsQuality);
        }

        [Test]
        public void SetNotifications_PersistsAcrossRecreation()
        {
            _go = new GameObject("SettingsService");
            { var svc = _go.AddComponent<SettingsService>(); RunAwake(svc); }

            SettingsService.Instance.SetNotifications(false);
            Object.DestroyImmediate(_go);
            _go = null;

            _go = new GameObject("SettingsService");
            { var svc = _go.AddComponent<SettingsService>(); RunAwake(svc); }

            Assert.IsFalse(SettingsService.Instance.Current.notificationsEnabled);
        }

        // ── Immediate effect ─────────────────────────────────────────────────

        [Test]
        public void SetMasterVolume_UpdatesCurrentImmediately()
        {
            _go = new GameObject("SettingsService");
            { var svc = _go.AddComponent<SettingsService>(); RunAwake(svc); }

            SettingsService.Instance.SetMasterVolume(0.6f);
            Assert.AreEqual(0.6f, SettingsService.Instance.Current.masterVolume, 0.001f);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        static void RunAwake(MonoBehaviour mb)
        {
            var t = mb.GetType();
            while (t != null && t != typeof(MonoBehaviour))
            {
                var m = t.GetMethod("Awake",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (m != null) { m.Invoke(mb, null); return; }
                t = t.BaseType;
            }
        }
    }
}
