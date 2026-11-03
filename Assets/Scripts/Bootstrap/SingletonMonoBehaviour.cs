using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Generic singleton base class for MonoBehaviours.
    /// Handles duplicate detection, Instance assignment, and optional scene persistence.
    /// Override PersistAcrossScenes to return true for singletons that should call DontDestroyOnLoad.
    /// When subclasses need additional Awake logic, override Awake and call base.Awake() first,
    /// then guard with: if (Instance != this) return;
    /// </summary>
    public abstract class SingletonMonoBehaviour<T> : MonoBehaviour where T : MonoBehaviour
    {
        public static T Instance { get; private set; }

        protected virtual bool PersistAcrossScenes => false;

        protected virtual void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // A duplicate is expected when a PersistAcrossScenes singleton survives a scene reload:
                // the kept instance lives in Unity's special "DontDestroyOnLoad" scene and the freshly
                // loaded scene's copy is redundant. Only a same-scene double-placement is a wiring bug
                // worth an error.
                string msg = $"[{typeof(T).Name}] DUPLICATE detected — destroying on '{gameObject.name}', keeping '{Instance.gameObject.name}'";
                if (Instance.gameObject.scene.name == "DontDestroyOnLoad")
                    GameLogger.Debug(msg + " (expected on scene reload)");
                else
                    GameLogger.Error(msg);

                if (Application.isPlaying)
                    Destroy(this);
                else
                    DestroyImmediate(this);
                return;
            }
            Instance = this as T;
            if (PersistAcrossScenes && Application.isPlaying)
                DontDestroyOnLoad(gameObject);
        }

        protected virtual void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }
    }
}
