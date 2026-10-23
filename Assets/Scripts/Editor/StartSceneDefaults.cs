using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Keeps the Editor on SplashScene. Play Mode always starts there (playModeStartScene), and SplashScene
/// opens once per Editor session when Unity starts. Playtests and manual play both depend on the splash
/// scene being the entry point, so a session that opens on another scene would fail.
/// </summary>
[InitializeOnLoad]
internal static class StartSceneDefaults
{
    private const string SplashPath = "Assets/Scenes/SplashScene.unity";
    private const string OpenedKey  = "StartSceneDefaults.SplashOpened";

    static StartSceneDefaults()
    {
        // CI and batchmode test runs must not touch the open scene.
        if (Application.isBatchMode) return;
        EditorApplication.delayCall += Apply;
    }

    private static void Apply()
    {
        var splash = AssetDatabase.LoadAssetAtPath<SceneAsset>(SplashPath);
        if (splash == null)
        {
            Debug.LogWarning($"[StartSceneDefaults] {SplashPath} not found; Play Mode start scene not set.");
            return;
        }

        // Not persisted by Unity, so set it again on every domain reload.
        EditorSceneManager.playModeStartScene = splash;

        // SessionState survives domain reloads, so this opens the splash scene once per Editor session.
        if (SessionState.GetBool(OpenedKey, false)) return;
        SessionState.SetBool(OpenedKey, true);

        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var active = EditorSceneManager.GetActiveScene();
        if (active.path == SplashPath) return;

        // Never discard unsaved edits to whatever scene the user was working on.
        if (active.isDirty)
        {
            Debug.Log($"[StartSceneDefaults] Unsaved changes in {active.name}; not opening {SplashPath}.");
            return;
        }
        EditorSceneManager.OpenScene(SplashPath);
    }
}
