using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Global, game-wide production multipliers applied on top of per-building bonuses (managers etc.).
    /// Lives as a singleton on the player entity (next to <see cref="PlayerInventoryTag"/>).
    ///
    /// Currently fed only by the megastructure's completed-stage rewards: <see cref="MegastructureService"/>
    /// recomputes and writes this whenever a stage completes or a save is loaded. Both multipliers default
    /// to 1 (no effect). Burst systems read it directly (guarded by HasSingleton), so it must stay blittable.
    /// </summary>
    public struct GlobalProductionBonus : IComponentData
    {
        public float OutputMult; // multiplies deposited output quantity; 1 = no bonus
        public float SpeedMult;  // multiplies craft progress per second; 1 = no bonus
    }
}
