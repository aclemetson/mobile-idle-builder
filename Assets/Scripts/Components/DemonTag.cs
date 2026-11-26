using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Marks Maxwell's Demon: the fixed entropy sink baked from the SubScene (BuildingSO.isEntropySink
    /// without isPlacedSink). Everything that treats the Demon as a fixture keys on this tag, not on
    /// <see cref="EntropySinkTag"/>: kept when the grid is cleared, never restored from a save, never
    /// moved or demolished, its own interaction panel. A placed sink such as Chemistry's Calorimeter
    /// carries <see cref="EntropySinkTag"/> only, so EntropySinkSystem drains it but it saves, restores
    /// and demolishes like any building. In stream worlds (WorldSO.streamTransport) the Demon is disabled.
    /// </summary>
    public struct DemonTag : IComponentData { }
}
