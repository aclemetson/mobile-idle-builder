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

        public static void BuildAndroid()
        {
            string outputDir = Path.Combine(Directory.GetCurrentDirectory(), "build", "Android");
            Directory.CreateDirectory(outputDir);
            string outputPath = Path.Combine(outputDir, $"{Application.productName}.aab");

            ConfigureAndroidKeystore();

            var options = new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            };

            PlayerSettings.Android.useCustomKeystore = true;
            EditorUserBuildSettings.buildAppBundle = true;

            BuildReport report = BuildPipeline.BuildPlayer(options);

            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"Android build failed: {report.summary.result}");
                EditorApplication.Exit(1);
            }
            else
            {
                Debug.Log($"Android build succeeded: {outputPath}");
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
