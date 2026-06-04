using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Scene-level singleton that holds global config references and enforces startup order.
    /// Add this to a persistent GameObject in the main scene alongside RecipeDatabase.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class GameBootstrap : SingletonMonoBehaviour<GameBootstrap>
    {
        protected override bool PersistAcrossScenes => true;

        [SerializeField] public GameConfigSO gameConfig;

#if UNITY_EDITOR
        [Header("Testing (Editor only)")]
        [Tooltip("Enables deterministic testing mode: fields are placed using a fixed seed and background saves (auto/pause/quit) are suppressed. Explicit SaveLocal() calls still work.")]
        [SerializeField] bool _testModeEnabled = false;

        [Tooltip("Seed passed to UnityEngine.Random.InitState before field placement shuffle. Change to get a different but repeatable layout.")]
        [SerializeField] int _testModeFieldSeed = 42;

        public static bool TestModeEnabled  => Instance != null && Instance._testModeEnabled;
        public static int  TestModeFieldSeed => Instance != null ? Instance._testModeFieldSeed : 42;
#endif
    }
}
