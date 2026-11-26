#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace MobileIdleBuilder.Editor
{
    /// <summary>
    /// Xcode-side setup for the App Tracking Transparency prompt (AdConsent + Plugins/iOS/TrackingAuthorization.mm):
    /// writes NSUserTrackingUsageDescription into Info.plist (iOS kills the app on the request without it, and
    /// App Review rejects a build that links ATT without it) and weak-links AppTrackingTransparency.framework
    /// into UnityFramework, where the plugin is compiled. SKAdNetwork ids are added by the LevelPlay package's
    /// own post-process (NetworkManagerSettings.AddNetworksSkadnetworkID, on by default).
    /// </summary>
    public static class IOSTrackingPostProcess
    {
        internal const string UsageDescription =
            "This lets us show you ads that are more relevant to you and measure how our ads perform.";

        [PostProcessBuild(3)]
        public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS) return;

            string plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            plist.root.SetString("NSUserTrackingUsageDescription", UsageDescription);
            plist.WriteToFile(plistPath);

            string projectPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            project.AddFrameworkToProject(project.GetUnityFrameworkTargetGuid(), "AppTrackingTransparency.framework", weak: true);
            project.WriteToFile(projectPath);

            Debug.Log("[IOSTrackingPostProcess] Added NSUserTrackingUsageDescription and AppTrackingTransparency.framework.");
        }
    }
}
#endif
