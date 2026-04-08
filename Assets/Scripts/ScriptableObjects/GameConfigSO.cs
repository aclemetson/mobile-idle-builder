using UnityEngine;

namespace MobileIdleBuilder
{
    [CreateAssetMenu(fileName = "game_config", menuName = "MobileIdleBuilder/Config/GameConfig")]
    public class GameConfigSO : ScriptableObject
    {
        [Header("Craft Time")]
        public float baseCraftTimeMultiplier = 1.0f;
        public float manualCraftTimeBase = 1.0f;

        [Header("Power Economy")]
        public float atomicAssemblerEVPerMassUnit = 5f;
        public float isotopicManipulatorEVPerNeutron = 8f;

        [Header("Prestige")]
        public float netWorthToPrestigeCurrencyRate = 1.0f;
        public float prestigeWallMultiplier = 10.0f;

        [Header("Environment")]
        public BuildEnvironment environment;
        public string apiBaseUrl;

        [Header("Decay Particles")]
        public float alphaParticleEVValue = 20f;
        public float betaParticleEVValue = 10f;
    }
}
