using NUnit.Framework;
using Unity.Entities;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Edit Mode ECS tests for TierProgress — the hook that advances PlayerProgressData.CurrentTier.
    ///
    /// Before this existed nothing raised CurrentTier (the only writes were PrestigeSystem/PVPSystem
    /// resetting it to 1), so it was pinned at 1 for the whole game. That silently flatlined both tier
    /// telemetry signals: tier_reached never fired, and highest_tier was a constant 1 on every
    /// player_snapshot and prestige_completed event.
    ///
    /// The item→tier lookup is stubbed: ItemDatabase populates its tables in Awake, and this assembly
    /// is editor-only, so no MonoBehaviour lifecycle runs.
    /// </summary>
    [TestFixture]
    public class TierProgressTests
    {
        World  _world;
        World  _previousDefaultWorld;
        Entity _player;

        const int SubatomicItem = 101;   // tier 1
        const int MolecularItem = 303;   // tier 3
        const int ComponentItem = 505;   // tier 5
        const int UnknownItem   = 999;   // not in the database

        [SetUp]
        public void SetUp()
        {
            _previousDefaultWorld = World.DefaultGameObjectInjectionWorld;

            _world = new World("TierProgressTest");
            World.DefaultGameObjectInjectionWorld = _world;

            _player = _world.EntityManager.CreateEntity();
            _world.EntityManager.AddComponentData(_player, new PlayerProgressData { CurrentTier = 1 });

            TierProgress.TierLookupOverrideForTests = id => id switch
            {
                SubatomicItem => 1,
                MolecularItem => 3,
                ComponentItem => 5,
                _             => 0,   // unknown item
            };
        }

        [TearDown]
        public void TearDown()
        {
            TierProgress.TierLookupOverrideForTests = null;
            World.DefaultGameObjectInjectionWorld = _previousDefaultWorld;
            if (_world.IsCreated) _world.Dispose();
        }

        int CurrentTier() =>
            _world.EntityManager.GetComponentData<PlayerProgressData>(_player).CurrentTier;

        [Test]
        public void ProducingAHigherTierItem_RaisesCurrentTier()
        {
            TierProgress.NotifyItemProduced(MolecularItem);

            Assert.AreEqual(3, CurrentTier());
        }

        [Test]
        public void ProducingALowerTierItem_LeavesCurrentTierAlone()
        {
            TierProgress.NotifyItemProduced(ComponentItem);
            TierProgress.NotifyItemProduced(SubatomicItem);

            Assert.AreEqual(5, CurrentTier(), "Tier is a high-water mark — it must never fall back.");
        }

        [Test]
        public void ProducingTheSameTierAgain_LeavesCurrentTierAlone()
        {
            TierProgress.NotifyItemProduced(MolecularItem);
            TierProgress.NotifyItemProduced(MolecularItem);

            Assert.AreEqual(3, CurrentTier());
        }

        [Test]
        public void ProducingAnUnknownItem_LeavesCurrentTierAlone()
        {
            TierProgress.NotifyItemProduced(UnknownItem);

            Assert.AreEqual(1, CurrentTier(), "An item with no tier must not reset or advance progress.");
        }

        [Test]
        public void TierClimbsAcrossSuccessiveProductions()
        {
            TierProgress.NotifyItemProduced(SubatomicItem);
            Assert.AreEqual(1, CurrentTier());

            TierProgress.NotifyItemProduced(MolecularItem);
            Assert.AreEqual(3, CurrentTier());

            TierProgress.NotifyItemProduced(ComponentItem);
            Assert.AreEqual(5, CurrentTier());
        }
    }
}
