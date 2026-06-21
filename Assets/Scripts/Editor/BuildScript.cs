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

        // Development build for the internal testing track: defines DEVELOPMENT_BUILD so the
        // dev console and dev-only tooling are compiled in. Uses BuildOptions.Development ONLY
        // (no AllowDebugging) so the manifest stays non-debuggable -- Google Play rejects
        // android:debuggable="true" AABs at upload.
        public static void BuildAndroidDevelopment() => BuildAndroidInternal(appBundle: true, development: true);

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

        private static void BuildAndroidInternal(bool appBundle, bool development = false)
        {
            string version   = PlayerSettings.bundleVersion;
            string ext       = appBundle ? "aab" : "apk";
            string outputDir = Path.Combine(Directory.GetCurrentDirectory(), "build", "Android");
            Directory.CreateDirectory(outputDir);
            string outputPath = Path.Combine(outputDir, $"{Application.productName}-v{version}.{ext}");

            ConfigureAndroidKeystore();

            PlayerSettings.Android.useCustomKeystore = true;
            EditorUserBuildSettings.buildAppBundle = appBundle;

            // Development defines DEVELOPMENT_BUILD (dev console + tooling). Deliberately no
            // AllowDebugging: it sets android:debuggable="true", which Google Play rejects.
            var buildOptions = development ? BuildOptions.Development : BuildOptions.None;
            GameLogger.Info($"Android build options: {buildOptions}");

            var options = new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = buildOptions,
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
