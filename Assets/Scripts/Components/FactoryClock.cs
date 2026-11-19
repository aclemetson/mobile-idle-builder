using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// How fast the factory simulation runs: 2 while a speed burst (premium shop Speed-Ups or the
    /// Production Surge ad) is active, else 1. ProductionSystem, CollectorSystem and ConveyorSystem
    /// multiply their delta time by it, so crafting, harvesting and belts all run at the same faster
    /// clock and belts never bottleneck a boosted factory. Research timers, the craft queue, field
    /// cooldowns and every real-time countdown stay on wall time.
    /// Written by <see cref="FactoryClockSystem"/>; systems read it with TryGetSingleton and treat a
    /// missing or non-positive value as 1 (test worlds never create it). Must stay blittable (Burst).
    /// </summary>
    public struct FactoryClock : IComponentData
    {
        public float Rate; // 1 = normal speed

        /// <summary>The rate to use: <paramref name="rate"/> when positive, else 1.</summary>
        public static float Sanitize(float rate) => rate > 0f ? rate : 1f;
    }
}
