using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MobileIdleBuilder.Editor
{
    /// <summary>
    /// One-shot editor utility that wires the Phase 5 Worlds panel into GameScene: adds the
    /// <see cref="WorldsSubController"/> component to the HUD GameObject (the one that already carries
    /// HUDController + SitesSubController). [RequireComponent] does NOT retro-add a sub-controller to an
    /// existing scene object, so this must be done explicitly — mirrors how the other sub-controllers
    /// are attached. Idempotent: a no-op if the component is already present.
    /// </summary>
    public static class WorldsPanelSceneWiring
    {
        private const string ScenePath = "Assets/Scenes/GameScene.unity";

        [MenuItem("MobileIdleBuilder/Wire Worlds Panel Into Scene")]
        public static void Wire()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var hud = Object.FindAnyObjectByType<HUDController>();
            if (hud == null)
            {
                Debug.LogError("[WorldsPanelSceneWiring] HUDController not found in GameScene — cannot wire Worlds panel.");
                return;
            }

            if (hud.GetComponent<WorldsSubController>() != null)
            {
                Debug.Log("[WorldsPanelSceneWiring] WorldsSubController already present on the HUD GameObject — no change.");
                return;
            }

            hud.gameObject.AddComponent<WorldsSubController>();
            EditorUtility.SetDirty(hud.gameObject);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[WorldsPanelSceneWiring] Added WorldsSubController to the HUD GameObject and saved GameScene.");
        }

        /// <summary>Batch entrypoint (-executeMethod): import game data, wire the scene, then exit.</summary>
        public static void ImportAndWireBatch()
        {
            int code = 0;
            try
            {
                GameDataImporter.Import();   // pick up the new dialogues + per-world themes
                Wire();
                AssetDatabase.SaveAssets();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[WorldsPanelSceneWiring] Batch wiring failed: {e}");
                code = 1;
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
