using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Singleton component on the player entity.
    /// Tracks prestige state that survives across resets.
    /// </summary>
    public struct PrestigeData : IComponentData
    {
        public int RunCount;                // number of completed prestiges
        public long PrestigeCurrency;       // total accumulated (spent + unspent)
        public long PrestigeCurrencySpent;  // for display only
        public float SpeedMultiplier;       // from purchased persistent upgrades
        public float OutputMultiplier;      // from purchased persistent upgrades
        public float CostReduction;         // building cost reduction (0.0–1.0)
    }
}
