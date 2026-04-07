using Unity.Entities;

namespace MobileIdleBuilder
{
    public struct ItemData : IComponentData
    {
        public int ItemID;
        public int Quantity;
        public int TierLevel;
    }
}
