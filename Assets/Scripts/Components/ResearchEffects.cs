using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Global multipliers granted by unlocked research (e.g. Transportation I = +10% belt speed).
    /// Singleton on the player entity, baked at 1 in <c>PlayerInventoryAuthoring</c> and rewritten by
    /// <see cref="ResearchService"/> whenever the unlocked set changes. Must stay blittable.
    /// Kept separate from <see cref="GlobalProductionBonus"/>, which MegastructureService overwrites whole.
    /// </summary>
    public struct ResearchEffects : IComponentData
    {
        public float ConveyorSpeedMult; // multiplies belt item speed; 1 = no bonus
    }
}
