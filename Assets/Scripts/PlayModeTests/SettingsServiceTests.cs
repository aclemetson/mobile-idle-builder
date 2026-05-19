using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MobileIdleBuilder.PlayModeTests
{
    /// <summary>
    /// PlayMode tests for SettingsService (MonoBehaviour lifecycle, mutation persistence).
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

        // [UnityTearDown] (not [TearDown]) is required for IEnumerator return type.
        // The yield lets OnDestroy fire so SingletonMonoBehaviour.Instance is null before the next SetUp.
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_go != null)
            {
                Object.Destroy(_go);
                _go = null;
                yield return null;
            }

            if (File.Exists(_settingsPath)) File.Delete(_settingsPath);
            if (_backup != null) File.WriteAllText(_settingsPath, _backup);
        }

        // ── Defaults ─────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator DefaultSettings_UsedOnFirstRun()
        {
            _go = new GameObject("SettingsService");
            _go.AddComponent<SettingsService>();
            yield return null;

            var s = SettingsService.Instance.Current;
            Assert.AreEqual(1f,   s.masterVolume,         "Default masterVolume should be 1");
            Assert.AreEqual(1f,   s.sfxVolume,            "Default sfxVolume should be 1");
            Assert.AreEqual(1f,   s.musicVolume,          "Default musicVolume should be 1");
            Assert.AreEqual(2,    s.graphicsQuality,      "Default graphicsQuality should be 2 (High)");
            Assert.IsTrue(s.notificationsEnabled,         "Default notificationsEnabled should be true");
        }

        // ── Persistence ───────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator SetMasterVolume_PersistsAcrossRecreation()
        {
            _go = new GameObject("SettingsService");
            _go.AddComponent<SettingsService>();
            yield return null;

            SettingsService.Instance.SetMasterVolume(0.4f);
            Object.Destroy(_go);
            _go = null;
            yield return null;

            _go = new GameObject("SettingsService");
            _go.AddComponent<SettingsService>();
            yield return null;

            Assert.AreEqual(0.4f, SettingsService.Instance.Current.masterVolume, 0.001f);
        }

        [UnityTest]
        public IEnumerator SetSFXVolume_PersistsAcrossRecreation()
        {
            _go = new GameObject("SettingsService");
            _go.AddComponent<SettingsService>();
            yield return null;

            SettingsService.Instance.SetSFXVolume(0.25f);
            Object.Destroy(_go);
            _go = null;
            yield return null;

            _go = new GameObject("SettingsService");
            _go.AddComponent<SettingsService>();
            yield return null;

            Assert.AreEqual(0.25f, SettingsService.Instance.Current.sfxVolume, 0.001f);
        }

        [UnityTest]
        public IEnumerator SetGraphicsQuality_PersistsAcrossRecreation()
        {
            _go = new GameObject("SettingsService");
            _go.AddComponent<SettingsService>();
            yield return null;

            SettingsService.Instance.SetGraphicsQuality(0);
            Object.Destroy(_go);
            _go = null;
            yield return null;

            _go = new GameObject("SettingsService");
            _go.AddComponent<SettingsService>();
            yield return null;

            Assert.AreEqual(0, SettingsService.Instance.Current.graphicsQuality);
        }

        [UnityTest]
        public IEnumerator SetNotifications_PersistsAcrossRecreation()
        {
            _go = new GameObject("SettingsService");
            _go.AddComponent<SettingsService>();
            yield return null;

            SettingsService.Instance.SetNotifications(false);
            Object.Destroy(_go);
            _go = null;
            yield return null;

            _go = new GameObject("SettingsService");
            _go.AddComponent<SettingsService>();
            yield return null;

            Assert.IsFalse(SettingsService.Instance.Current.notificationsEnabled);
        }

        // ── Immediate effect ─────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator SetMasterVolume_UpdatesCurrentImmediately()
        {
            _go = new GameObject("SettingsService");
            _go.AddComponent<SettingsService>();
            yield return null;

            SettingsService.Instance.SetMasterVolume(0.6f);
            Assert.AreEqual(0.6f, SettingsService.Instance.Current.masterVolume, 0.001f);
        }
    }
}
