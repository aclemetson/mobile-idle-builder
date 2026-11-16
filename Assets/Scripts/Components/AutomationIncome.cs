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
        /// <summary>Everything automation paid, Cyclotron Kick boost included (the Statistics rate).</summary>
        public double Total;

        /// <summary>
        /// The same income with the Cyclotron Kick boost divided out. The Entropy Surge prices itself from
        /// this, so a 5-minute boost does not inflate a 10-minute payout.
        /// </summary>
        public double Unboosted;
    }
}
