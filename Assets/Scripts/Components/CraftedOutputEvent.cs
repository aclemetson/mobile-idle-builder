using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// One completed recipe output, recorded by <see cref="ProductionSystem"/> at the moment it deposits
    /// and drained the same frame by <see cref="ProductionAchievementBridge"/>.
    ///
    /// The bridge used to infer crafts by diffing BuildingOutputSlot totals between frames, which loses
    /// them: ConveyorSystem and the bridge are both merely UpdateAfter(ProductionSystem) with no ordering
    /// between them, so whenever a belt drains the output buffer before the bridge samples it, the total is
    /// unchanged and the craft is never counted. A hungry belt is the steady state of any real factory, so
    /// automated production credited craft achievements only sporadically.
    ///
    /// Recording at the source is exact — the item, and the quantity actually deposited (output multipliers
    /// included) — and immune to system ordering.
    /// </summary>
    [InternalBufferCapacity(4)]
    public struct CraftedOutputEvent : IBufferElementData
    {
        public int ItemID;
        public int Quantity;
    }
}
