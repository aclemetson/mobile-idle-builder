using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Per-consumer power state, written every frame by <see cref="PowerGridSystem"/> and read by
    /// ProductionSystem (to gate/throttle crafting) and the visualizers (powered/unpowered tint).
    /// <para>
    /// IsConnected = 1 when the consumer's footprint is within the influence radius of any generator.
    /// ThrottleRatio is the speed multiplier applied to production: 0 when disconnected, otherwise the
    /// global supply/draw ratio (1 when supply meets demand, &lt;1 during a brownout).
    /// </para>
    /// </summary>
    public struct PowerStatus : IComponentData
    {
        public byte  IsConnected;
        public float ThrottleRatio;
    }
}
