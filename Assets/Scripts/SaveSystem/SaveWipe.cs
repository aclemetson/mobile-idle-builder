using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Canonical save-wipe routines shared by the editor menu (MobileIdleBuilder -> Clear Save)
    /// and the in-game dev console ("clear save" / "clear cloud save" / "clear all save"). Keeps
    /// every entry point deleting the exact same files and PlayerPrefs keys so a wipe behaves
    /// identically wherever it is triggered from.
    /// </summary>
    public static class SaveWipe
    {
        /// <summary>
        /// Every file the game writes to <see cref="Application.persistentDataPath"/> outside of
        /// save.json — each one owned by a service that reloads it on boot, and each one therefore
        /// able to outlive a save wipe and carry stale state into a "fresh" run.
        /// </summary>
        /// <summary>
        /// Test seam for the PlayerPrefs clear. Tests must not really call PlayerPrefs.DeleteAll —
        /// it would wipe the editor's own prefs (cached sign-in included) on every suite run.
        /// </summary>
        internal static System.Action ClearAllPrefsOverrideForTests;

        static void ClearAllPrefs() => (ClearAllPrefsOverrideForTests ?? PlayerPrefs.DeleteAll)();

        static IEnumerable<string> SidecarFiles => new[]
        {
            RecipeKnowledgeService.FileName,   // recipes unlocked in past prestige runs
            SettingsService.FileName,          // audio / graphics / camera preferences
            FeatureFlagService.FileName,       // cached remote-config flags
        };

        /// <summary>
        /// Deletes the local save file (save.json + .bak) and the auth/idle PlayerPrefs keys.
        /// When <paramref name="scheduleCloudWipe"/> is true, sets <see cref="SaveManager.k_WipePending"/>
        /// so <c>SaveManager.ReconcileWithCloud()</c> deletes the cloud key on the next boot.
        /// Used by the editor tool (which cannot reach UGS in edit mode) and as the offline
        /// fallback for the dev console when an immediate cloud delete fails.
        ///
        /// Leaves the sidecar files alone — see <see cref="WipeEverything"/> for a full reset.
        /// </summary>
        public static void WipeFilesAndPrefs(bool scheduleCloudWipe)
        {
            new LocalSaveService().Delete();

            PlayerPrefs.DeleteKey(SaveManager.BackgroundTimestampKey);
            PlayerPrefs.DeleteKey(AuthSessionPolicy.k_LastOpenedKey);
            PlayerPrefs.DeleteKey(AuthSessionPolicy.k_LastFullAuthKey);

            if (scheduleCloudWipe)
                PlayerPrefs.SetInt(SaveManager.k_WipePending, 1);

            PlayerPrefs.Save();
        }

        /// <summary>
        /// Full fresh-install reset: save.json (+ .bak), every sidecar file, and ALL PlayerPrefs —
        /// including the UGS/auth keys, so the next boot starts from nothing the way a first-ever
        /// launch does. The cloud key is the caller's job (dev console deletes it over the network
        /// first); pass <paramref name="scheduleCloudWipe"/> when that delete failed so the wipe is
        /// retried on the next boot.
        ///
        /// PlayerPrefs.DeleteAll is deliberate: the targeted DeleteKey list in WipeFilesAndPrefs only
        /// covers the keys we happen to know about, and anything a service adds later would silently
        /// survive a "clear all". The cost is that cached sign-in state goes too.
        ///
        /// Returns the files it actually deleted, for the caller to report.
        /// </summary>
        public static List<string> WipeEverything(bool scheduleCloudWipe)
        {
            var deleted = new List<string>();

            var local = new LocalSaveService();
            if (local.Exists()) deleted.Add("save.json");
            local.Delete();

            foreach (var name in SidecarFiles)
            {
                string path = Path.Combine(Application.persistentDataPath, name);
                if (!File.Exists(path)) continue;
                File.Delete(path);
                deleted.Add(name);
            }

            ClearAllPrefs();

            // Re-set after the clear, or it would be wiped along with everything else.
            if (scheduleCloudWipe)
                PlayerPrefs.SetInt(SaveManager.k_WipePending, 1);

            PlayerPrefs.Save();

            return deleted;
        }
    }
}
