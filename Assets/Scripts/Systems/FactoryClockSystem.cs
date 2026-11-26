using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Keeps the <see cref="FactoryClock"/> singleton in step with the speed-burst window
    /// (<c>SaveData.speedBoostExpiryUtc</c>). Runs first in the simulation group so every factory
    /// system sees this frame's rate. Managed (reads SaveManager), so not Burst-compiled.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    public partial class FactoryClockSystem : SystemBase
    {
        protected override void OnCreate()
        {
            EntityManager.CreateSingleton(new FactoryClock { Rate = 1f }, "FactoryClock");
        }

        protected override void OnUpdate()
        {
            float rate = PremiumShopCalculator.GetSpeedBoostMultiplier(SaveManager.Instance?.Current?.speedBoostExpiryUtc);
            var clock = SystemAPI.GetSingleton<FactoryClock>();
            if (clock.Rate == rate) return;
            clock.Rate = rate;
            SystemAPI.SetSingleton(clock);
            GameLogger.Info($"[FactoryClock] Factory running at {rate:0.#}x.");
        }
    }
}
