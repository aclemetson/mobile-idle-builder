using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Buffer element baked onto each building entity — one per recipe output.
    /// </summary>
    public struct RecipeOutputSlot : IBufferElementData
    {
        public int ItemID;
        public int Quantity;
    }
}
