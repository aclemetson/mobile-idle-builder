using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Present on a building entity that has a manager assigned. Carries the baked bonus state so
    /// the bonus can be applied and removed exactly (no float division to undo).
    /// <para>
    /// CraftSpeed bonuses are baked directly into <see cref="BuildingData.ProductionSpeed"/>; the
    /// pre-bonus speed is stored here so unassign (and re-apply after an upgrade resets the speed)
    /// restores the exact original value. OutputQuantity and PowerDiscount are read live from this
    /// component by ProductionSystem / the power-draw path, so they are stored as multipliers here.
    /// </para>
    /// </summary>
    public struct ManagerAssignmentData : IComponentData
    {
        /// <summary>Index into ManagerDatabaseSO.allManagers of the assigned manager.</summary>
        public int   ManagerIndex;
        /// <summary>BuildingData.ProductionSpeed before any CraftSpeed multiplier was baked in.
        /// For non-CraftSpeed managers this equals the building's current speed (restore is a no-op).</summary>
        public float PreBonusSpeed;
        /// <summary>Recipe output multiplier applied at deposit time (1 = no change).</summary>
        public float AppliedOutputMult;
        /// <summary>Fraction of eV draw kept (1 = no discount, 0.8 = -20% power).</summary>
        public float AppliedPowerMult;
    }
}
