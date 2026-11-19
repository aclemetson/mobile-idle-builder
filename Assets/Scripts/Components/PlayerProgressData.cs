using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Singleton component tracking the player's current-run state.
    /// Reset on prestige (except fields explicitly preserved by PrestigeSystem).
    /// </summary>
    public struct PlayerProgressData : IComponentData
    {
        public int CurrentTier;             // highest tier the player has reached this run
        public long BaseCurrency;           // spendable currency earned this run
        public long TotalEntropySpent;      // cumulative entropy actually paid on research/buildings/upgrades this run
        public long TotalEntropyDiscounted; // list price minus price paid, summed over every discounted purchase this run (saved)
        public double StoredItemValue;      // value of items waiting in building inputs/staging/outputs; NetWorthSystem, not saved
        public double NetWorth;             // calculated each frame: inventory + stored items + BaseCurrency + TotalEntropySpent + TotalEntropyDiscounted + BaseNetWorth
        public double BaseNetWorth;         // dev-skip floor; always 0 in normal play
        public double PrestigeWallValue;    // populated from GameConfigSO at bake time; 0 = no wall
        public bool PrestigeAvailable;      // set true by PrestigeSystem when wall is hit
        public bool PrestigeRequested;      // set true by UI when player taps prestige button
        public bool PVPRunRequested;        // set true by UI when player enters competition
    }
}
