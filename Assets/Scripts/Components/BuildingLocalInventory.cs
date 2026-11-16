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
    /// Creation buffer: the components of the craft a building is about to make (or is making). Holds at
    /// most each recipe input's quantity. ProductionSystem tops it up from <see cref="BuildingInputSlot"/>
    /// every frame and a finished craft consumes it, so the input buffer (capped per item) can queue
    /// the next craft meanwhile. Lets a recipe that needs more than the input cap (Uranium: 146
    /// Neutrons against 30) still craft. Rules in <see cref="CraftStaging"/>. Not saved, like the input.
    /// </summary>
    public struct CraftStagingSlot : IBufferElementData
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
        public int InputCapacity;  // max items PER ITEM TYPE in the input buffer (default 20); see InputAcceptance
    }
}
