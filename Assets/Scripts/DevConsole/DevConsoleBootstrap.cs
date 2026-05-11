using UnityEngine;

namespace MobileIdleBuilder.Dev
{
    /// <summary>
    /// Instantiates the dev console prefab at startup in Editor and Development builds.
    /// The serialized field is always compiled so the scene layout is consistent across
    /// build configurations — avoids the "different serialization layout" deserialization
    /// error that occurs when the entire class is #if-guarded.
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public sealed class DevConsoleBootstrap : MonoBehaviour
    {
        [SerializeField] private GameObject devConsolePrefab;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
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
#endif
    }
}

