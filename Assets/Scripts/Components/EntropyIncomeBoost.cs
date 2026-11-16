using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Live multiplier on entropy the factory earns (Demon sinks, surplus autosell, escaping byproducts).
    /// Currently fed only by the Cyclotron Kick boost (<see cref="CyclotronProgress.BoostMultiplier"/>):
    /// <see cref="CyclotronController"/> creates the singleton at runtime and rewrites it whenever the boost
    /// starts or runs out. Systems read it with TryGetSingleton and treat a missing or non-positive value as 1.
    /// Must stay blittable (read from Burst).
    /// </summary>
    public struct EntropyIncomeBoost : IComponentData
    {
        public float Mult; // 1 = no boost
    }
}
