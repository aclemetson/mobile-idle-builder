#if UNITY_IOS
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Callbacks;
using UnityEngine;

namespace MobileIdleBuilder.Editor
{
    /// <summary>
    /// Fails the iOS build if the generated Podfile is missing a CocoaPod that a package
    /// asked for.
    ///
    /// The Podfile belongs to the Mobile Dependency Resolver (EDM4U): it collects every
    /// &lt;iosPod&gt; element across the *Dependencies.xml files and writes the Podfile at
    /// PostProcessBuild order 40. scripts/archive-upload-ios.sh then runs `pod install` on
    /// the macOS runner and archives the .xcworkspace.
    ///
    /// A post-process step that rewrote that Podfile instead of appending to it once
    /// dropped LevelPlay's IronSourceSDK and IronSourceUnityAdsAdapter pods. Nothing
    /// noticed until Xcode failed on the macOS runner, half an hour later, with
    /// "'IronSource/LPMAdInfo.h' file not found". This check runs in the 40-50 window EDM4U
    /// documents for Podfile edits -- after generation, before `pod install` -- so that
    /// mistake instead fails the cheap Linux build job with the missing pods named.
    /// </summary>
    public static class IOSPodfileVerifier
    {
        [PostProcessBuild(45)]
        public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS)
                return;

            string podfilePath = Path.Combine(pathToBuiltProject, "Podfile");
            if (!File.Exists(podfilePath))
            {
                throw new BuildFailedException(
                    $"[IOSPodfileVerifier] No Podfile at {podfilePath}. EDM4U should have generated " +
                    "one (Assets > External Dependency Manager > iOS Resolver > Settings > Podfile " +
                    "generation). Without a Podfile, archive-upload-ios.sh skips `pod install` and " +
                    "the Xcode archive fails to link every native SDK.");
            }

            string podfile = File.ReadAllText(podfilePath);

            List<string> expected = DeclaredPods();
            if (expected.Count == 0)
            {
                // Nothing to check against means this guard is silently inert -- the exact
                // failure mode it exists to prevent. The project always has at least the
                // GoogleSignIn and IronSource pods, so an empty scan is a bug here, not a
                // project with no pods.
                throw new BuildFailedException(
                    "[IOSPodfileVerifier] Found no <iosPod> declarations under Assets. Expected at " +
                    "least GoogleSignIn and the IronSource pods, so the *Dependencies.xml scan is " +
                    "broken -- fix it rather than shipping an unverified Podfile.");
            }

            var missing = new List<string>();
            foreach (string pod in expected)
            {
                if (!podfile.Contains($"pod '{pod}'"))
                    missing.Add(pod);
            }

            if (missing.Count > 0)
            {
                throw new BuildFailedException(
                    $"[IOSPodfileVerifier] The generated Podfile is missing {missing.Count} pod(s) " +
                    $"declared in *Dependencies.xml: {string.Join(", ", missing)}. A post-process step " +
                    "has almost certainly overwritten the Podfile rather than appending to it -- edits " +
                    $"must happen at PostProcessBuild order 40-50.\n--- Podfile ---\n{podfile}");
            }

            // The native bridges in Assets/Plugins/iOS (GoogleSignIn.mm, the LevelPlay
            // wrappers) compile into UnityFramework and expect the pods linked statically
            // into it rather than as nested dynamic frameworks.
            if (!podfile.Contains("use_frameworks!"))
            {
                GameLogger.Warning(
                    "[IOSPodfileVerifier] Podfile has no use_frameworks! directive; expected " +
                    "`use_frameworks! :linkage => :static` (EDM4U's default).");
            }

            GameLogger.Info($"[IOSPodfileVerifier] Podfile OK - {expected.Count} pod(s): {string.Join(", ", expected)}");
        }

        // Read the pod list back out of the same *Dependencies.xml files EDM4U reads, so
        // adding an SDK never requires updating a hand-maintained list here.
        static List<string> DeclaredPods()
        {
            var pods = new List<string>();

            foreach (string path in Directory.GetFiles(Application.dataPath, "*Dependencies.xml", SearchOption.AllDirectories))
            {
                XDocument doc;
                try
                {
                    doc = XDocument.Load(path);
                }
                catch (Exception e)
                {
                    GameLogger.Warning($"[IOSPodfileVerifier] Skipping unparseable {path}: {e.Message}");
                    continue;
                }

                foreach (XElement pod in doc.Descendants("iosPod"))
                {
                    string name = (string)pod.Attribute("name");
                    if (!string.IsNullOrEmpty(name) && !pods.Contains(name))
                        pods.Add(name);
                }
            }

            return pods;
        }
    }
}
#endif
