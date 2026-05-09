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
                GameLogger.Error($"[{typeof(T).Name}] DUPLICATE detected — destroying on '{gameObject.name}', keeping '{Instance.gameObject.name}'");
                Destroy(this);
                return;
            }
            Instance = this as T;
            if (PersistAcrossScenes)
                DontDestroyOnLoad(gameObject);
        }

        protected virtual void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }
    }
}
