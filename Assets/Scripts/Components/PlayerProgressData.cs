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
        public float NetWorth;              // calculated from all placed buildings + inventory
        public bool PrestigeAvailable;      // set true by PrestigeSystem when wall is hit
        public bool PrestigeRequested;      // set true by UI when player taps prestige button
        public bool PVPRunRequested;        // set true by UI when player enters competition
    }
}
