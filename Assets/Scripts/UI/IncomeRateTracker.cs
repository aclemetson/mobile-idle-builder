using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Samples <see cref="AutomationIncome"/> once per real second and exposes a rolling 60 s rate.
    /// Added to the HUD GameObject by HUDController.Awake; runs even while the Statistics panel is closed.
    /// </summary>
    public class IncomeRateTracker : MonoBehaviour
    {
        public const double WindowSeconds = 60.0;

        private readonly RollingRate _rate = new RollingRate(90);
        private EntityQuery _query;
        private bool        _queryValid;
        private World       _world;
        private float       _nextSample;

        /// <summary>Entropy per second earned by automation, averaged over the last 60 s.</summary>
        public double PerSecond => _rate.Rate(WindowSeconds);

        void Update()
        {
            if (Time.unscaledTime < _nextSample) return;
            _nextSample = Time.unscaledTime + 1f;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) { _queryValid = false; return; }

            if (!_queryValid || _world != world)
            {
                _world = world;
                _query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<AutomationIncome>());
                _queryValid = true;
            }

            if (_query.CalculateEntityCount() != 1) return;   // singleton not baked yet (SubScene race)
            var income = _query.GetSingleton<AutomationIncome>();
            _rate.Add(Time.unscaledTime, income.Total);
        }
    }
}
