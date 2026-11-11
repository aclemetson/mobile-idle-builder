using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Added to field-collector buildings. Drives autonomous item production
    /// at a fixed rate without requiring the manual IsCrafting trigger.
    /// </summary>
    public struct CollectorData : IComponentData
    {
        public float OutputRate; // items per second (from BuildingSO.baseOutputRate)
        public float Timer;      // accumulator, seconds since last output
        public float OutputCarry; // fraction of an item owed by a fractional output multiplier (see OutputCarry)
    }
}
