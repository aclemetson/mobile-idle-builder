using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.PlayModeTests
{
    /// <summary>
    /// Covers SaveWipe.WipeEverything — the "clear all save" fresh-install reset.
    ///
    /// The bug this guards against: a wipe that only removes save.json leaves the sidecar files
    /// (recipe knowledge, settings, cached flags) on disk, so the "fresh" run silently inherits state
    /// from the old one. Every file the game writes to persistentDataPath must go.
    ///
    /// The PlayerPrefs clear is stubbed via ClearAllPrefsOverrideForTests — calling the real
    /// DeleteAll here would wipe the editor's own prefs (cached sign-in included) on every run.
    /// </summary>
    public class SaveWipeTests
    {
        static string PathTo(string fileName) =>
            Path.Combine(Application.persistentDataPath, fileName);

        static readonly string[] AllFiles =
        {
            "save.json",
            "save.json.bak",
            RecipeKnowledgeService.FileName,
            SettingsService.FileName,
            FeatureFlagService.FileName,
        };

        bool _prefsCleared;

        [SetUp]
        public void SetUp()
        {
            _prefsCleared = false;
            SaveWipe.ClearAllPrefsOverrideForTests = () => _prefsCleared = true;

            foreach (var f in AllFiles)
                File.WriteAllText(PathTo(f), "{}");
        }

        [TearDown]
        public void TearDown()
        {
            SaveWipe.ClearAllPrefsOverrideForTests = null;

            foreach (var f in AllFiles)
                if (File.Exists(PathTo(f))) File.Delete(PathTo(f));

            PlayerPrefs.DeleteKey(SaveManager.k_WipePending);
            PlayerPrefs.Save();
        }

        [Test]
        public void WipeEverything_DeletesSaveAndEverySidecarFile()
        {
            SaveWipe.WipeEverything(scheduleCloudWipe: false);

            foreach (var f in AllFiles)
                Assert.IsFalse(File.Exists(PathTo(f)), $"{f} survived the wipe");
        }

        [Test]
        public void WipeEverything_ClearsPlayerPrefs()
        {
            SaveWipe.WipeEverything(scheduleCloudWipe: false);

            Assert.IsTrue(_prefsCleared,
                "Prefs must be cleared wholesale — a targeted DeleteKey list only covers the keys we " +
                "happen to know about, and anything added later would survive a 'clear all'.");
        }

        [Test]
        public void WipeEverything_SchedulesCloudWipe_AfterClearingPrefs()
        {
            SaveWipe.WipeEverything(scheduleCloudWipe: true);

            Assert.AreEqual(1, PlayerPrefs.GetInt(SaveManager.k_WipePending, 0),
                "The pending-cloud-wipe flag must be re-set AFTER the prefs clear, or an offline " +
                "wipe silently leaves the cloud save in place.");
        }

        [Test]
        public void WipeEverything_DoesNotScheduleCloudWipe_WhenCloudDeleteSucceeded()
        {
            SaveWipe.WipeEverything(scheduleCloudWipe: false);

            Assert.AreEqual(0, PlayerPrefs.GetInt(SaveManager.k_WipePending, 0));
        }

        [Test]
        public void WipeEverything_ReportsTheFilesItDeleted()
        {
            var deleted = SaveWipe.WipeEverything(scheduleCloudWipe: false);

            CollectionAssert.Contains(deleted, "save.json");
            CollectionAssert.Contains(deleted, RecipeKnowledgeService.FileName);
            CollectionAssert.Contains(deleted, SettingsService.FileName);
            CollectionAssert.Contains(deleted, FeatureFlagService.FileName);
        }

        [Test]
        public void WipeEverything_IsSafe_WhenNothingIsOnDisk()
        {
            foreach (var f in AllFiles)
                if (File.Exists(PathTo(f))) File.Delete(PathTo(f));

            var deleted = SaveWipe.WipeEverything(scheduleCloudWipe: false);

            Assert.IsEmpty(deleted, "Nothing was on disk, so nothing should be reported as deleted.");
        }
    }
}
