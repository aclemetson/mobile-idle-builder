using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Marks the singleton entity that owns the player's global InventorySlot buffer.
    /// </summary>
    public struct PlayerInventoryTag : IComponentData { }
}
