using NUnit.Framework;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>Research effects: the pure sum/describe helpers and the ConveyorSystem speed multiplier.</summary>
    [TestFixture]
    public class ResearchEffectsTests
    {
        private static ResearchSO MakeResearch(params ResearchEffect[] effects)
        {
            var so = ScriptableObject.CreateInstance<ResearchSO>();
            so.effects = effects;
            return so;
        }

        private static ResearchEffect Conveyor(float v) =>
            new ResearchEffect { type = ResearchEffectType.ConveyorSpeed, value = v };

        [Test]
        public void SumEffect_AddsSameTypeAcrossResearch()
        {
            var list = new[] { MakeResearch(Conveyor(0.10f)), MakeResearch(Conveyor(0.05f)) };
            Assert.AreEqual(0.15f, ResearchService.SumEffect(list, ResearchEffectType.ConveyorSpeed), 1e-6f);
        }

        [Test]
        public void SumEffect_EmptyOrNull_IsZero()
        {
            Assert.AreEqual(0f, ResearchService.SumEffect(new ResearchSO[0], ResearchEffectType.ConveyorSpeed));
            Assert.AreEqual(0f, ResearchService.SumEffect(null, ResearchEffectType.ConveyorSpeed));
            Assert.AreEqual(0f, ResearchService.SumEffect(new[] { MakeResearch() }, ResearchEffectType.ConveyorSpeed));
        }

        [Test]
        public void Describe_FormatsBeltSpeedPercent()
        {
            Assert.AreEqual("+10% belt speed", ResearchEffectText.Describe(Conveyor(0.10f)));
            Assert.AreEqual("+7.5% belt speed", ResearchEffectText.Describe(Conveyor(0.075f)));
        }

        // ── ConveyorSystem integration ───────────────────────────────────────

        private static float ProgressAfterTick(float? speedMult)
        {
            var world = new World("ResearchEffectsTest");
            try
            {
                var em = world.EntityManager;
                var simGroup = world.CreateSystemManaged<SimulationSystemGroup>();
                simGroup.AddSystemToUpdateList(world.CreateSystem<ConveyorSystem>());
                simGroup.SortSystems();

                if (speedMult.HasValue)
                {
                    var fx = em.CreateEntity(typeof(ResearchEffects));
                    em.SetComponentData(fx, new ResearchEffects { ConveyorSpeedMult = speedMult.Value });
                }

                var seg = em.CreateEntity();
                em.AddComponentData(seg, new ConveyorSegmentData
                {
                    Cell = new int2(0, 0), EntryDir = 0, ExitDir = 0,
                    NextSegment = Entity.Null, PrevSegment = Entity.Null,
                    TransportTime = 1f, IsChainHead = true
                });
                ConveyorItems.Add(em, seg, 7, 0f);

                world.SetTime(new TimeData(elapsedTime: 0, deltaTime: 0.25f));
                world.Update();
                return ConveyorItems.Front(em, seg).Progress;
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void ConveyorSystem_AppliesSpeedMultiplier()
        {
            float baseline = ProgressAfterTick(null);
            float boosted  = ProgressAfterTick(1.1f);
            Assert.AreEqual(0.25f, baseline, 1e-5f);
            Assert.AreEqual(baseline * 1.1f, boosted, 1e-5f);
        }
    }
}
