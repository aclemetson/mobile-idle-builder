using System.IO;
using UnityEditor;
using UnityEngine;

namespace MobileIdleBuilder.Editor
{
    /// <summary>
    /// MobileIdleBuilder → Clear Save (Fresh Start)
    ///
    /// Deletes the local save file and schedules a cloud save wipe on the next play-mode
    /// boot. This mirrors the in-game dev-console "clear cloud save" command but works
    /// without needing to enter play mode first.
    ///
    /// Cloud deletion is deferred because UGS must be initialized (requires play mode).
    /// SaveManager.ReconcileWithCloud() checks the PlayerPrefs flag and handles it.
    /// </summary>
    public static class SaveWipeEditorTool
    {
        static string SavePath => Path.Combine(Application.persistentDataPath, "save.json");

        [MenuItem("MobileIdleBuilder/Clear Save (Fresh Start)")]
        static void ClearSave()
        {
            bool confirm = EditorUtility.DisplayDialog(
                "Clear Save Data",
                $"This will:\n" +
                $"  • Delete local save file\n" +
                $"  • Wipe cloud save on next Play\n" +
                $"  • Reset auth session timestamps\n\n" +
                $"Save path:\n{SavePath}",
                "Clear Everything", "Cancel");

            if (!confirm) return;

            // Shared wipe: deletes save.json + .bak and the auth/idle PlayerPrefs keys, and
            // schedules a cloud wipe for the next play-mode boot (UGS isn't reachable in edit mode).
            bool hadLocal = File.Exists(SavePath);
            SaveWipe.WipeFilesAndPrefs(scheduleCloudWipe: true);

            string localStatus = hadLocal ? "deleted" : "not found (already clean)";
            Debug.Log($"[SaveWipe] Local save {localStatus}. Cloud save will be wiped on next Play session.");

            EditorUtility.DisplayDialog(
                "Done",
                $"Local save {localStatus}.\n\nCloud save will be wiped automatically when you next enter Play Mode.",
                "OK");
        }
    }
}
