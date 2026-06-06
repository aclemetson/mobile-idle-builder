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
        public float prestigeBaseValue = 5000f;
        public float prestigeWallMultiplier = 10.0f;
        [Tooltip("Multiplier in: PC = floor(log10(netWorth / prestigeBaseValue) × scale)")]
        public float prestigeCurrencyScale = 50f;

        [Header("Environment")]
        public BuildEnvironment environment;
        public string apiBaseUrl;

        [Header("Decay Particles")]
        public float alphaParticleEVValue = 20f;
        public float betaParticleEVValue = 10f;

        [Header("Starting State")]
        public long startingEntropy;

        [Header("Building Purchase Scaling")]
        [Tooltip("Each additional placement of the same Tier 1 building costs base × multiplier^(n-1)")]
        public float buildingPurchaseMultiplierT1 = 1.5f;
        [Tooltip("Each additional placement of the same Tier 2 building costs base × multiplier^(n-1)")]
        public float buildingPurchaseMultiplierT2 = 1.4f;
        [Tooltip("Each additional placement of the same Tier 3+ building costs base × multiplier^(n-1)")]
        public float buildingPurchaseMultiplierT3Plus = 1.3f;
    }
}
