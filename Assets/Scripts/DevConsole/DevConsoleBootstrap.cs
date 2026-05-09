#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

namespace MobileIdleBuilder.Dev
{
    /// <summary>
    /// Instantiates the dev console prefab at startup. Attach to the same persistent
    /// GameObject as GameBootstrap (or any scene-level object). Assign the DevConsole
    /// prefab (UIDocument + DevConsoleController) in the Inspector.
    /// Entire class is stripped from release builds.
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public sealed class DevConsoleBootstrap : MonoBehaviour
    {
        [SerializeField] private GameObject devConsolePrefab;

        void Awake()
        {
            if (devConsolePrefab == null)
            {
                GameLogger.Warning("[DevConsole] devConsolePrefab not assigned on DevConsoleBootstrap.");
                return;
            }

            var go = Instantiate(devConsolePrefab);
            go.name = "[DevConsole]";
            DontDestroyOnLoad(go);
        }
    }
}
#endif
