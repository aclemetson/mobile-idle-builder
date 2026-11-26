using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// One batch of items Maxwell's Demon consumed this frame. <see cref="EntropySinkSystem"/> appends to the
    /// buffer on the <see cref="DemonDeliveryLog"/> entity (it still pays entropy as before), and
    /// <see cref="WorldTaskService"/> drains it every frame to credit the active world task (Chemistry gate).
    /// </summary>
    [InternalBufferCapacity(8)]
    public struct DemonDelivery : IBufferElementData
    {
        public int ItemID;
        public int Quantity;
    }

    /// <summary>Tags the single entity that holds the <see cref="DemonDelivery"/> buffer (created by EntropySinkSystem).</summary>
    public struct DemonDeliveryLog : IComponentData { }
}
