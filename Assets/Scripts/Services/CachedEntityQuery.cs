using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// An <see cref="EntityQuery"/> created once per ECS world and reused, for MonoBehaviour / static
    /// code that reads ECS on a hot path (#285). Creating a query on every call (and forgetting to
    /// dispose it) was common: e.g. TierProgress ran once per crafted item, and
    /// PowerConnectionGraph.GatherFromEcs every 0.4 s while the power overlay was open.
    ///
    /// The query is owned by the world it was created in, so it is freed with that world; never
    /// Dispose() the returned query. If the world changes (scene reload, test worlds) or is destroyed,
    /// the next <see cref="For"/> call builds a fresh query for the current world.
    ///
    /// Main thread only (like all EntityManager access from managed code).
    /// </summary>
    public sealed class CachedEntityQuery
    {
        readonly ComponentType[] _types;
        World       _world;
        EntityQuery _query;

        public CachedEntityQuery(params ComponentType[] types) => _types = types;

        /// <summary>The query for <paramref name="em"/>'s world, created on first use per world.</summary>
        public EntityQuery For(EntityManager em)
        {
            var world = em.World;
            if (_world == null || _world != world || !_world.IsCreated)
            {
                _query = em.CreateEntityQuery(_types);
                _world = world;
            }
            return _query;
        }
    }
}
