using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Stores the grid footprint (width x height in cells) for buildings larger than 1x1.
    /// Only added when the BuildingSO has a footprint other than (1,1).
    /// The GridPosition cell is the bottom-left corner of the footprint.
    /// </summary>
    public struct BuildingFootprint : IComponentData
    {
        public int Width;
        public int Height;
    }
}
