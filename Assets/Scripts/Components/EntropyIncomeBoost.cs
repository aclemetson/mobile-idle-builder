using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Live multiplier on entropy the factory earns (Demon sinks, surplus autosell, escaping byproducts).
    /// Fed by the Cyclotron Kick boost (<see cref="CyclotronProgress.BoostMultiplier"/>) times the ad-free bonus
    /// (<see cref="AdFree.IncomeMultiplier"/>, #503): <see cref="CyclotronController"/> creates the singleton at
    /// runtime and rewrites it whenever either changes. Systems read it with TryGetSingleton and treat a missing or non-positive value as 1.
    /// Must stay blittable (read from Burst).
    /// </summary>
    public struct EntropyIncomeBoost : IComponentData
    {
        public float Mult; // 1 = no boost
    }
}
