using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Marker component on Maxwell's Demon building entities.
    /// EntropySinkSystem queries this tag to identify buildings that consume
    /// any items deposited into their BuildingInputSlot buffer and convert
    /// them to entropy (PlayerProgressData.BaseCurrency).
    /// </summary>
    public struct EntropySinkTag : IComponentData { }
}
