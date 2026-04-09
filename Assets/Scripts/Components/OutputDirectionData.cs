using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Stores the output direction for buildings that eject items onto a conveyor.
    /// Only added to entities whose BuildingSO has placementRule == MustBeOnField.
    /// Cast Direction to/from OutputDirection enum.
    /// </summary>
    public struct OutputDirectionData : IComponentData
    {
        public int Direction; // OutputDirection enum value
    }
}
