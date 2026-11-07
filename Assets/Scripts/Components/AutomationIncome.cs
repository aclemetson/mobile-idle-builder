using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Running total of entropy earned by automation this session: Maxwell's Demon sink, belt surplus
    /// sales and escaping byproducts. Singleton on the player entity, baked at 0 in
    /// <c>PlayerInventoryAuthoring</c>, never saved. The HUD's IncomeRateTracker samples it to show a
    /// rolling per-second rate. Must stay blittable (written from Burst).
    /// </summary>
    public struct AutomationIncome : IComponentData
    {
        public double Total;
    }
}
