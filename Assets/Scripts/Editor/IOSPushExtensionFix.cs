#if UNITY_IOS
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace MobileIdleBuilder.Editor
{
    /// <summary>
    /// Repairs the rich-push Notification Service Extension target that com.unity.services.push-notifications
    /// adds to the Xcode project (IOSRichPushNotificationPostProcess, PostProcessBuild order 1).
    ///
    /// Package 4.0.2 writes the extension's ARCHS as "$(ARCHS_STANDARD" (closing parenthesis missing), so Xcode
    /// would not resolve it to the standard architectures. This runs right after the package and writes the
    /// intended value. It only touches that one setting and only when it is the broken one, so a fixed package
    /// version makes this a no-op. Re-check after updating the push package.
    /// </summary>
    public static class IOSPushExtensionFix
    {
        const string ExtensionTargetName = "notificationservice";
        const string StandardArchs = "$(ARCHS_STANDARD)";

        [PostProcessBuild(2)]
        public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS) return;

            string projectPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);

            string extension = project.TargetGuidByName(ExtensionTargetName);
            if (string.IsNullOrEmpty(extension))
            {
                Debug.LogWarning($"[IOSPushExtensionFix] No '{ExtensionTargetName}' target in the Xcode project; nothing to fix.");
                return;
            }

            string archs = project.GetBuildPropertyForAnyConfig(extension, "ARCHS");
            if (archs == StandardArchs) return;

            project.SetBuildProperty(extension, "ARCHS", StandardArchs);
            project.WriteToFile(projectPath);
            Debug.Log($"[IOSPushExtensionFix] Extension ARCHS '{archs}' -> '{StandardArchs}'.");
        }
    }
}
#endif
