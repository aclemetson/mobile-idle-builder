using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Output inventory buffer on building entities.
    /// Production writes here instead of the global InventorySlot singleton.
    /// Conveyors pull items from this buffer.
    /// </summary>
    public struct BuildingOutputSlot : IBufferElementData
    {
        public int ItemID;
        public int Quantity;
    }

    /// <summary>
    /// Input inventory buffer on building entities.
    /// Conveyors deposit items here.
    /// Production reads recipe inputs from this buffer (falls back to global inventory when empty).
    /// </summary>
    public struct BuildingInputSlot : IBufferElementData
    {
        public int ItemID;
        public int Quantity;
    }

    /// <summary>
    /// Configuration for a building's local inventory.
    /// Added to every building entity by BuildingPlacer.
    /// </summary>
    public struct BuildingInventoryConfig : IComponentData
    {
        public int OutputCapacity; // max total items in output buffer (default 20)
        public int InputCapacity;  // max total items in input buffer  (default 20)
    }
}
