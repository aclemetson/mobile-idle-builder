using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Carries the building's <see cref="BuildingStructureKind"/> onto its entity so
    /// <c>BuildingVisualizer</c> can pick the bespoke procedural structure without hardcoding building
    /// ids. Added by <c>BuildingPlacer</c> at placement (and therefore on load, which re-runs placement)
    /// for any building whose BuildingSO declares a non-None structureKind.
    /// Collectors and entropy sinks keep being detected via their gameplay components
    /// (CollectorData / EntropySinkTag); this is for the bespoke producer forms.
    /// </summary>
    public struct BuildingVisualStyle : IComponentData
    {
        public int Kind; // BuildingStructureKind enum value
    }
}
