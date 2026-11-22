using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Empties the run-scoped entities of the default ECS world before GameScene is reloaded.
    ///
    /// The default world outlives a scene reload, so anything left in it is drawn in the new scene and
    /// written back by the next save. Prestige (<see cref="PrestigeSystem"/>) and taking a cloud save while
    /// the game runs (<see cref="SaveManager"/>, live path) both reload the scene, and both must clear the
    /// same state: every building except the kept fixtures (Maxwell's Demon, <see cref="EntropySinkTag"/>),
    /// every belt segment with the item riding on it, and what the kept fixtures hold. Before this was
    /// shared, the cloud swap skipped it and the cloud grid loaded on top of the device's own buildings.
    ///
    /// The player inventory is not touched: <c>ECSLoadBridge.ApplyLoadedSave</c> rewrites it on load, and
    /// prestige clears it itself.
    /// </summary>
    public static class RunStateClearer
    {
        /// <summary>Destroys run buildings and belts and empties the kept fixtures. Returns the buildings destroyed.</summary>
        public static int ClearLiveRunEntities(EntityManager em)
        {
            using var buildings = em.CreateEntityQuery(new EntityQueryDesc
            {
                All  = new[] { ComponentType.ReadOnly<BuildingData>() },
                None = new[] { ComponentType.ReadOnly<DemonTag>() },
            });
            int buildingCount = buildings.CalculateEntityCount();
            em.DestroyEntity(buildings);

            using var belts = em.CreateEntityQuery(ComponentType.ReadOnly<ConveyorSegmentData>());
            em.DestroyEntity(belts);

            using var sinks = em.CreateEntityQuery(ComponentType.ReadOnly<DemonTag>());
            var sinkEntities = sinks.ToEntityArray(Unity.Collections.Allocator.Temp);
            foreach (var e in sinkEntities)
            {
                if (em.HasBuffer<BuildingInputSlot>(e))  em.GetBuffer<BuildingInputSlot>(e).Clear();
                if (em.HasBuffer<BuildingOutputSlot>(e)) em.GetBuffer<BuildingOutputSlot>(e).Clear();
            }
            sinkEntities.Dispose();

            return buildingCount;
        }
    }
}
