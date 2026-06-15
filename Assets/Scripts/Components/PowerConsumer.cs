using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Present on a building that requires power to operate (BuildingSO.requiresPower).
    /// DrawEV is the nameplate eV draw for the building's current upgrade level, baked at
    /// placement / re-baked on upgrade. The live draw used by PowerGridSystem is this value
    /// multiplied by any manager PowerDiscount (<see cref="ManagerAssignmentData.AppliedPowerMult"/>).
    /// </summary>
    public struct PowerConsumer : IComponentData
    {
        public float DrawEV;
    }
}
