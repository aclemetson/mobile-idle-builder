using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Canonical save-wipe routine shared by the editor menu (MobileIdleBuilder -> Clear Save)
    /// and the in-game dev console ("clear save" / "clear cloud save"). Keeps both entry points
    /// deleting the exact same files and PlayerPrefs keys so a wipe behaves identically.
    /// </summary>
    public static class SaveWipe
    {
        /// <summary>
        /// Deletes the local save file (save.json + .bak) and the auth/idle PlayerPrefs keys.
        /// When <paramref name="scheduleCloudWipe"/> is true, sets <see cref="SaveManager.k_WipePending"/>
        /// so <c>SaveManager.ReconcileWithCloud()</c> deletes the cloud key on the next boot.
        /// Used by the editor tool (which cannot reach UGS in edit mode) and as the offline
        /// fallback for the dev console when an immediate cloud delete fails.
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
    }
}
