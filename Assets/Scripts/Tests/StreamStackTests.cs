using NUnit.Framework;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Stream stacks (Chemistry, 2026-11-27): a bank building drops its products onto the stream in stacks of 1-5
    /// (once Gas Compression is researched), a stack rides as one item, and a building takes it only when its input
    /// has room for all of it. Rules in <see cref="StreamStackRules"/>; ECS in ConveyorSystem's stream passes.
    /// </summary>
    public class StreamStackTests
    {
        const int North = 0, East = 1, South = 2;

        // ── Rules ─────────────────────────────────────────────────────────────

        [Test]
        public void Effective_IsTheSetting_CappedByResearch()
        {
            Assert.AreEqual(1, StreamStackRules.Effective(5, StreamStackRules.MaxUnlocked(0)), "before the research: singles");
            Assert.AreEqual(1, StreamStackRules.Effective(5, 1));
            Assert.AreEqual(3, StreamStackRules.Effective(3, 5));
            Assert.AreEqual(5, StreamStackRules.Effective(9, 5), "never above Max");
            Assert.AreEqual(1, StreamStackRules.Effective(0, 5), "an old save's 0 is 1");
        }

        [Test]
        public void PickDrop_WaitsForAFullStack()
        {
            Assert.AreEqual(-1, StreamStackRules.PickDrop(new[] { 2 }, 3, total: 2, outputCap: 20, out _));
            Assert.AreEqual(0,  StreamStackRules.PickDrop(new[] { 4 }, 3, total: 4, outputCap: 20, out int q));
            Assert.AreEqual(3, q, "drops exactly one stack and keeps the rest");
        }

        [Test]
        public void PickDrop_TakesTheFirstOutputWithAFullStack()
        {
            Assert.AreEqual(1, StreamStackRules.PickDrop(new[] { 2, 5 }, 3, total: 7, outputCap: 20, out int q));
            Assert.AreEqual(3, q);
        }

        [Test]
        public void PickDrop_FullOutputWithNoFullStack_DropsTheBiggestPart()
        {
            // A building never stops just because no single product reached its stack size.
            Assert.AreEqual(1, StreamStackRules.PickDrop(new[] { 1, 2 }, 5, total: 3, outputCap: 3, out int q));
            Assert.AreEqual(2, q);
        }

        [Test]
        public void HasRoomFor_NeedsRoomForTheWholeStack()
        {
            Assert.IsTrue(StreamStackRules.HasRoomFor(7, 3, 10));
            Assert.IsFalse(StreamStackRules.HasRoomFor(8, 3, 10));
        }

        [Test]
        public void VisualScale_GrowsWithTheStack()
        {
            Assert.AreEqual(1f,    StreamStackRules.VisualScale(1), 1e-5f);
            Assert.AreEqual(1.32f, StreamStackRules.VisualScale(5), 1e-5f);
        }

        [Test]
        public void StackColor_NoneForOne_ADistinctColourForEachLargerStack()
        {
            Assert.AreEqual(0f, StreamStackRules.StackColor(1).a, "a single unit has no glow");
            var seen = new System.Collections.Generic.HashSet<UnityEngine.Color>();
            for (int q = 2; q <= StreamStackRules.Max; q++)
            {
                var c = StreamStackRules.StackColor(q);
                Assert.AreEqual(1f, c.a, $"stack of {q} has a colour");
                Assert.IsTrue(seen.Add(c), $"stack of {q} has its own colour");
            }
        }

        [Test]
        public void Feedstocks_AreDrawnFromThePhysicsAtoms_BondedAsInLife()
        {
            var f = BeltItemVisuals.Feedstocks;
            Assert.AreEqual((6, 6, 1, 0), f["graphite"],     "graphite is the carbon atom");
            Assert.AreEqual((1, 0, 2, 1), f["hydrogen_gas"], "H-H: single bond");
            Assert.AreEqual((8, 8, 2, 2), f["oxygen_gas"],   "O=O: double bond");
            Assert.AreEqual((7, 7, 2, 3), f["nitrogen_gas"], "N≡N: triple bond");
        }

        [Test]
        public void DiatomicPair_FitsAStreamSlot()
        {
            // Two outer rings plus the gap between nuclei fit in DiatomicWidth, which is under the item spacing (0.5).
            Assert.LessOrEqual(BeltItemVisuals.DiatomicWidth * StreamStackRules.VisualScale(StreamStackRules.Max), 0.55f);
            Assert.Less(BeltItemVisuals.DiatomicSpacing, 2f, "the outer shells overlap, as in a covalent bond");
        }

        [Test]
        public void BondMesh_HasOneStickPerSharedPair()
        {
            int Verts(int order) => BeltItemVisuals.BondMesh(order, 0.1f, UnityEngine.Color.white).vertexCount;
            Assert.AreEqual(2 * Verts(1), Verts(2));
            Assert.AreEqual(3 * Verts(1), Verts(3));
        }

        [Test]
        public void ConveyorItem_Quantity_IsAtLeastOne()
        {
            Assert.AreEqual(1, new ConveyorItem { ItemID = 1 }.Quantity, "every ordinary belt item is one unit");
            Assert.AreEqual(4, new ConveyorItem { ItemID = 1, Stack = 4 }.Quantity);
        }

        [Test]
        public void GasCompression_IsChemistryResearch_ThatAllowsStacksOfFive()
        {
            var db = UnityEngine.Resources.Load<ResearchDatabaseSO>("ResearchDatabase");
            Assert.IsNotNull(db, "ResearchDatabase");
            ResearchSO gc = null;
            foreach (var r in db.allResearch) if (r != null && r.id == "gas_compression") gc = r;
            Assert.IsNotNull(gc, "gas_compression is imported");
            Assert.AreEqual(WorldGate.Chemistry, gc.requiredWorld);
            Assert.AreEqual("molecular_synthesis", gc.prerequisites[0].id, "it follows Carbon Chemistry");
            float total = ResearchService.SumEffect(new[] { gc }, ResearchEffectType.StreamStack);
            Assert.AreEqual(StreamStackRules.Max, StreamStackRules.MaxUnlocked(1 + (int)total), "stacks of 1-5 once done");
        }

        // ── ECS ───────────────────────────────────────────────────────────────

        World _world;
        EntityManager _em;

        [SetUp]
        public void SetUp()
        {
            _world = new World("StreamStackTest");
            _em    = _world.EntityManager;
            var sim = _world.CreateSystemManaged<SimulationSystemGroup>();
            sim.AddSystemToUpdateList(_world.CreateSystem<StreamFlowSystem>());
            sim.AddSystemToUpdateList(_world.CreateSystem<ConveyorSystem>());
            sim.SortSystems();
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        void Tick(float dt = 0.02f)
        {
            _world.SetTime(new TimeData(elapsedTime: 0, deltaTime: dt));
            _world.Update();
        }

        void Research(int maxStack)
        {
            var e = _em.CreateEntity();
            _em.AddComponentData(e, new ResearchEffects { ConveyorSpeedMult = 1f, MaxStreamStack = maxStack });
        }

        void Spring()
        {
            var e = _em.CreateEntity();
            _em.AddComponentData(e, new BuildingData { IsActive = true, ProductionSpeed = 1f });
            _em.AddComponentData(e, new GridPosition { Cell = new int2(0, 1) });
            _em.AddComponentData(e, new StreamSource { Flow = 10f });
            _em.AddBuffer<PlacedPortData>(e).Add(new PlacedPortData { PortType = (int)PortType.Output, CellX = 0, CellY = 1, Facing = South });
            _em.AddBuffer<BuildingOutputSlot>(e);
        }

        Entity[] Stream(int length = 4)
        {
            var segs = new Entity[length];
            for (int x = 0; x < length; x++)
            {
                segs[x] = _em.CreateEntity();
                _em.AddComponentData(segs[x], new GridPosition { Cell = new int2(x, 0) });
                _em.AddComponentData(segs[x], new StreamSegment());
                _em.AddBuffer<ConveyorItem>(segs[x]);
            }
            for (int x = 0; x < length; x++)
                _em.AddComponentData(segs[x], new ConveyorSegmentData
                {
                    Cell = new int2(x, 0), EntryDir = East, ExitDir = East, TransportTime = 1f,
                    NextSegment = x + 1 < length ? segs[x + 1] : Entity.Null,
                    PrevSegment = x > 0 ? segs[x - 1] : Entity.Null,
                    IsChainHead = x == 0,
                });
            return segs;
        }

        Entity Bank(int x, int stack = 1, int recipeInput = -1)
        {
            var e = _em.CreateEntity();
            _em.AddComponentData(e, new BuildingData { IsActive = true, ProductionSpeed = 1f });
            _em.AddComponentData(e, new GridPosition { Cell = new int2(x, 1) });
            var ports = _em.AddBuffer<PlacedPortData>(e);
            ports.Add(new PlacedPortData { PortType = (int)PortType.Input,  CellX = x, CellY = 1, Facing = North });
            ports.Add(new PlacedPortData { PortType = (int)PortType.Output, CellX = x, CellY = 1, Facing = South });
            _em.AddBuffer<BuildingInputSlot>(e);
            _em.AddBuffer<BuildingOutputSlot>(e);
            _em.AddComponentData(e, new BuildingInventoryConfig { InputCapacity = 10, OutputCapacity = 20 });
            _em.AddComponentData(e, new StreamConsumer { Draw = 1f });
            _em.AddComponentData(e, new StreamStatus());
            _em.AddComponentData(e, new StreamStack { Size = (byte)stack });
            _em.AddComponentData(e, new RecipeProcessData { RecipeID = recipeInput >= 0 ? 1 : -1 });
            var ri = _em.AddBuffer<RecipeInputSlot>(e);
            if (recipeInput >= 0) ri.Add(new RecipeInputSlot { ItemID = recipeInput, Quantity = 2 });
            return e;
        }

        void Output(Entity bldg, int itemId, int qty) =>
            _em.GetBuffer<BuildingOutputSlot>(bldg).Add(new BuildingOutputSlot { ItemID = itemId, Quantity = qty });

        [Test]
        public void Drop_WaitsUntilItHoldsAFullStack_ThenDropsItAsOneItem()
        {
            Research(5);
            Spring();
            var segs = Stream();
            var a = Bank(2, stack: 3);
            Output(a, 11, 2);

            Tick();
            Assert.AreEqual(0, _em.GetBuffer<ConveyorItem>(segs[2]).Length, "two of three: it waits");

            _em.GetBuffer<BuildingOutputSlot>(a).Clear();
            Output(a, 11, 4);
            Tick();

            var items = _em.GetBuffer<ConveyorItem>(segs[2]);
            Assert.AreEqual(1, items.Length, "the stack rides as one item");
            Assert.AreEqual(11, items[0].ItemID);
            Assert.AreEqual(3, items[0].Quantity);
            Assert.AreEqual(1, SlotBufferUtils.CountInOutputBuffer(_em.GetBuffer<BuildingOutputSlot>(a), 11), "one left over");
        }

        [Test]
        public void Drop_BeforeTheResearch_IsOneAtATime()
        {
            Spring();   // no ResearchEffects: Gas Compression not done
            var segs = Stream();
            var a = Bank(2, stack: 5);
            Output(a, 11, 5);

            Tick();

            var items = _em.GetBuffer<ConveyorItem>(segs[2]);
            Assert.AreEqual(1, items.Length);
            Assert.AreEqual(1, items[0].Quantity, "the setting does nothing until stacking is researched");
        }

        [Test]
        public void Pickup_TakesTheWholeStack_WhenItFits()
        {
            Research(5);
            Spring();
            var segs = Stream();
            var a = Bank(1, recipeInput: 5);
            SlotBufferUtils.AddToInputBuffer(_em.GetBuffer<BuildingInputSlot>(a), 5, 7);
            _em.GetBuffer<ConveyorItem>(segs[1]).Add(new ConveyorItem { ItemID = 5, Progress = 0.2f, Stack = 3 });

            Tick();

            Assert.AreEqual(10, SlotBufferUtils.CountInInputBuffer(_em.GetBuffer<BuildingInputSlot>(a), 5), "7 + a stack of 3");
            Assert.AreEqual(0, _em.GetBuffer<ConveyorItem>(segs[1]).Length);
        }

        [Test]
        public void Pickup_LeavesAStackThatDoesNotFit_FloatingOn()
        {
            Research(5);
            Spring();
            var segs = Stream();
            var a = Bank(1, recipeInput: 5);
            SlotBufferUtils.AddToInputBuffer(_em.GetBuffer<BuildingInputSlot>(a), 5, 8);
            _em.GetBuffer<ConveyorItem>(segs[1]).Add(new ConveyorItem { ItemID = 5, Progress = 0.2f, Stack = 3 });

            Tick();

            Assert.AreEqual(8, SlotBufferUtils.CountInInputBuffer(_em.GetBuffer<BuildingInputSlot>(a), 5), "room for 2, not 3");
            var left = _em.GetBuffer<ConveyorItem>(segs[1]);
            Assert.AreEqual(1, left.Length, "the stack floats on, whole");
            Assert.AreEqual(3, left[0].Quantity);
        }

        [Test]
        public void Mouth_DeliversTheWholeStackToTheCalorimeter()
        {
            Research(5);
            Spring();
            var segs = Stream();
            var sink = _em.CreateEntity();
            _em.AddComponentData(sink, new BuildingData { IsActive = true });
            _em.AddComponentData(sink, new GridPosition { Cell = new int2(4, 0) });
            _em.AddComponentData(sink, new EntropySinkTag());
            _em.AddBuffer<PlacedPortData>(sink).Add(new PlacedPortData { PortType = (int)PortType.Input, CellX = 4, CellY = 0, Facing = East });
            _em.AddBuffer<BuildingInputSlot>(sink);
            _em.AddComponentData(sink, new BuildingInventoryConfig { InputCapacity = 20, OutputCapacity = 0 });
            _em.GetBuffer<ConveyorItem>(segs[3]).Add(new ConveyorItem { ItemID = 5, Progress = 0.99f, Stack = 4 });

            Tick(0.1f);
            Tick(0.1f);

            Assert.AreEqual(4, SlotBufferUtils.CountInInputBuffer(_em.GetBuffer<BuildingInputSlot>(sink), 5), "all four units, not one");
        }
    }
}
