using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Scene-level singleton that holds global config references and enforces startup order.
    /// Add this to a persistent GameObject in the main scene alongside RecipeDatabase.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class GameBootstrap : MonoBehaviour
    {
        public static GameBootstrap Instance { get; private set; }

        [SerializeField] public GameConfigSO gameConfig;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError($"[GameBootstrap] DUPLICATE detected — destroying component on '{gameObject.name}', keeping '{Instance.gameObject.name}'");
                Destroy(this);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
    }
}
