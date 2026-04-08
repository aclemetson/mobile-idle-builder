using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Buffer element baked onto each building entity — one per recipe input.
    /// </summary>
    public struct RecipeInputSlot : IBufferElementData
    {
        public int ItemID;
        public int Quantity;
    }
}
