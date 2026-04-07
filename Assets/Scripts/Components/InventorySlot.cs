using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Buffer element on the singleton player inventory entity.
    /// </summary>
    public struct InventorySlot : IBufferElementData
    {
        public int ItemID;
        public int Quantity;
    }
}
