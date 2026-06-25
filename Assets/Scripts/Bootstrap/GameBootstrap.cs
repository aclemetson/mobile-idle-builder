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

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;

            // Overlay any cached remote balance overrides onto the shared GameConfig instance at boot,
            // before any system reads it (no-op when no blob is published). See GameDataOverrides.
            GameDataOverrides.ApplyConfig(gameConfig);
        }
    }
}
