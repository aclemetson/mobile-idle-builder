using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MobileIdleBuilder.Editor
{
    public static class BuildScript
    {
        private static readonly string[] Scenes = GetScenePaths();

        public static void BuildAndroid()    => BuildAndroidInternal(appBundle: true);
        public static void BuildAndroidApk() => BuildAndroidInternal(appBundle: false);

        // Internal testing build: compiles in the dev console and dev-only tooling by defining
        // the DEVELOPMENT_BUILD symbol, but produces a normal (non-debuggable) release bundle.
        // We do NOT use BuildOptions.Development -- in Unity 6 that sets android:debuggable="true"
        // in the manifest, which Google Play rejects ("APK is marked as debuggable").
        public static void BuildAndroidDevelopment() => BuildAndroidInternal(appBundle: true, devTooling: true);

        public static void BuildWindows()
        {
            string version    = PlayerSettings.bundleVersion;
            string outputDir  = Path.Combine(Directory.GetCurrentDirectory(), "build", "Windows");
            Directory.CreateDirectory(outputDir);
            string outputPath = Path.Combine(outputDir, $"{Application.productName}-v{version}.exe");

            var options = new BuildPlayerOptions
            {
                scenes           = Scenes,
                locationPathName = outputPath,
                target           = BuildTarget.StandaloneWindows64,
                options          = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                GameLogger.Error($"Windows build failed: {report.summary.result}");
                EditorApplication.Exit(1);
            }
            else
            {
                GameLogger.Info($"Windows build succeeded: {outputPath}");
                EditorApplication.Exit(0);
            }
        }

        public static void BuildIOS()
        {
            string outputDir = Path.Combine(Directory.GetCurrentDirectory(), "build", "iOS");
            Directory.CreateDirectory(outputDir);

            // iOS build output is a folder (Xcode project), not a single file.
            var options = new BuildPlayerOptions
            {
                scenes           = Scenes,
                locationPathName = outputDir,
                target           = BuildTarget.iOS,
                options          = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                GameLogger.Error($"iOS build failed: {report.summary.result}");
                EditorApplication.Exit(1);
            }
            else
            {
                GameLogger.Info($"iOS Xcode project generated: {outputDir}");
                EditorApplication.Exit(0);
            }
        }

        private static void BuildAndroidInternal(bool appBundle, bool devTooling = false)
        {
            string version   = PlayerSettings.bundleVersion;
            string ext       = appBundle ? "aab" : "apk";
            string outputDir = Path.Combine(Directory.GetCurrentDirectory(), "build", "Android");
            Directory.CreateDirectory(outputDir);
            string outputPath = Path.Combine(outputDir, $"{Application.productName}-v{version}.{ext}");

            ConfigureAndroidKeystore();

            EditorUserBuildSettings.buildAppBundle = appBundle;

            // Define DEVELOPMENT_BUILD for compilation only (dev console + tooling) via
            // extraScriptingDefines, while keeping options = BuildOptions.None so the bundle is a
            // normal release build. A real BuildOptions.Development build sets
            // android:debuggable="true", which Google Play rejects; this gives us the symbol
            // without the debuggable manifest. Nothing reads Debug.isDebugBuild at runtime.
            string[] extraDefines = devTooling ? new[] { "DEVELOPMENT_BUILD" } : null;
            GameLogger.Info($"Android build extra defines: {(extraDefines == null ? "(none)" : string.Join(";", extraDefines))}");

            var options = new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = BuildOptions.None,
                extraScriptingDefines = extraDefines,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);

            if (report.summary.result != BuildResult.Succeeded)
            {
                GameLogger.Error($"Android build failed: {report.summary.result}");
                EditorApplication.Exit(1);
            }
            else
            {
                GameLogger.Info($"Android build succeeded: {outputPath}");
                EditorApplication.Exit(0);
            }
        }

        private static void ConfigureAndroidKeystore()
        {
            string keystorePath = Path.Combine(Directory.GetCurrentDirectory(), "secrets", "user.keystore");

            // useCustomKeystore must be enabled BEFORE the keystore name and passwords are
            // assigned: Unity ignores the password setters while it is false, which yields an
            // unsigned AAB that Google Play rejects ("All uploaded bundles must be signed").
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = keystorePath;
            PlayerSettings.Android.keystorePass = GetEnvOrArg("ANDROID_KEYSTORE_PASS");
            PlayerSettings.Android.keyaliasName = GetEnvOrArg("ANDROID_KEY_ALIAS");
            PlayerSettings.Android.keyaliasPass = GetEnvOrArg("ANDROID_KEY_PASS");
        }

        private static string GetEnvOrArg(string name)
        {
            string value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrEmpty(value))
                throw new Exception($"Missing required environment variable: {name}");
            return value;
        }

        private static string[] GetScenePaths()
        {
            var scenes = new System.Collections.Generic.List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled)
                    scenes.Add(scene.path);
            }
            return scenes.ToArray();
        }
    }
}
