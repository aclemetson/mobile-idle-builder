using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Buffer element storing the world-space position and facing of a single placed port.
    /// Added to building entities that have ports defined in their BuildingSO.
    /// Cell coords are absolute grid positions; anchor is the building's GridPosition cell.
    /// </summary>
    public struct PlacedPortData : IBufferElementData
    {
        public int PortType; // PortType enum value
        public int CellX;
        public int CellY;
        public int Facing;   // OutputDirection enum value
    }
}
