using System.Collections.Generic;
using NUnit.Framework;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Chemistry streams: the pure flow and drop rules (<see cref="StreamRules"/>) and the ECS behaviour of
    /// <see cref="StreamFlowSystem"/> + ConveyorSystem's stream passes on a small hand-built stream:
    /// a Spring at (0,1) pours south onto (0,0); the stream runs east (0,0)..(3,0); bank buildings sit
    /// north of it at (x,1) with their input facing North (off the stream) and output facing South (onto it).
    /// </summary>
    [TestFixture]
    public class StreamTests
    {
        const int North = 0, East = 1, South = 2;

        // ── Pure rules ────────────────────────────────────────────────────────

        [Test]
        public void Allocate_UpstreamFirst_LastOnesStall()
        {
            var ratios = new float[3];
            float left = StreamRules.Allocate(10f, new[] { 6f, 6f, 1f }, ratios);
            Assert.AreEqual(1f, ratios[0]);
            Assert.AreEqual(4f / 6f, ratios[1], 1e-5f, "the building where the water runs out gets the rest");
            Assert.AreEqual(0f, ratios[2], "everything past it stalls");
            Assert.AreEqual(0f, left);
        }

        [Test]
        public void Allocate_EnoughFlow_EveryoneRuns_AndTheRestFlowsOn()
        {
            var ratios = new float[2];
            float left = StreamRules.Allocate(10f, new[] { 3f, 3f }, ratios);
            Assert.AreEqual(new[] { 1f, 1f }, ratios);
            Assert.AreEqual(4f, left);
        }

        [Test]
        public void Allocate_NoSource_NothingRuns()
        {
            var ratios = new float[2];
            StreamRules.Allocate(0f, new[] { 3f, 0f }, ratios, hasSource: false);
            Assert.AreEqual(new[] { 0f, 0f }, ratios);
        }

        [Test]
        public void DropIndex_EmptyTile_DropsInTheMiddle()
        {
            Assert.AreEqual(0, StreamRules.DropIndex(new List<float>(), 0.5f, 0.5f));
        }

        [Test]
        public void DropIndex_KeepsFrontFirstOrder()
        {
            Assert.AreEqual(1, StreamRules.DropIndex(new List<float> { 1.0f }, 0.5f, 0.5f), "behind the front item");
            Assert.AreEqual(0, StreamRules.DropIndex(new List<float> { 0.0f }, 0.5f, 0.5f), "ahead of the rear item");
        }

        [Test]
        public void DropIndex_NoGap_Waits()
        {
            Assert.AreEqual(-1, StreamRules.DropIndex(new List<float> { 0.7f }, 0.5f, 0.5f));
        }

        [Test]
        public void FlowLabel_WarnsWhenBuildingsAreShortOfWater()
        {
            Assert.AreEqual("Flow 6 / 10 L/s", HUDStatusBarController.FormatFlowLabel(6f, 10f, 0));
            StringAssert.StartsWith("⚠", HUDStatusBarController.FormatFlowLabel(12f, 10f, 1));
        }

        [Test]
        public void InspectorStreamStatus_NamesTheProblem()
        {
            Assert.AreEqual("Not beside a stream", HUDBuildingInspectorSubController.StreamStatusText(new StreamStatus()));
            StringAssert.StartsWith("Dry", HUDBuildingInspectorSubController.StreamStatusText(new StreamStatus { OnStream = 1 }));
            StringAssert.Contains("50%", HUDBuildingInspectorSubController.StreamStatusText(new StreamStatus { OnStream = 1, Ratio = 0.5f }));
            Assert.AreEqual("Flowing", HUDBuildingInspectorSubController.StreamStatusText(new StreamStatus { OnStream = 1, Ratio = 1f }));
        }

        // ── ECS ───────────────────────────────────────────────────────────────

        World _world;
        EntityManager _em;

        [SetUp]
        public void SetUp()
        {
            _world = new World("StreamTest");
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

        Entity Spring(float flow)
        {
            var e = _em.CreateEntity();
            _em.AddComponentData(e, new BuildingData { IsActive = true, ProductionSpeed = 1f });
            _em.AddComponentData(e, new GridPosition { Cell = new int2(0, 1) });
            _em.AddComponentData(e, new StreamSource { Flow = flow });
            _em.AddBuffer<PlacedPortData>(e).Add(new PlacedPortData { PortType = (int)PortType.Output, CellX = 0, CellY = 1, Facing = South });
            _em.AddBuffer<BuildingOutputSlot>(e);
            return e;
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

        Entity Bank(int x, float draw, int recipeInput = -1)
        {
            var e = _em.CreateEntity();
            _em.AddComponentData(e, new BuildingData { IsActive = true, ProductionSpeed = 1f });
            _em.AddComponentData(e, new GridPosition { Cell = new int2(x, 1) });
            var ports = _em.AddBuffer<PlacedPortData>(e);
            ports.Add(new PlacedPortData { PortType = (int)PortType.Input,  CellX = x, CellY = 1, Facing = North });
            ports.Add(new PlacedPortData { PortType = (int)PortType.Output, CellX = x, CellY = 1, Facing = South });
            _em.AddBuffer<BuildingInputSlot>(e);
            _em.AddBuffer<BuildingOutputSlot>(e);
            _em.AddComponentData(e, new BuildingInventoryConfig { InputCapacity = 10, OutputCapacity = 10 });
            _em.AddComponentData(e, new StreamConsumer { Draw = draw });
            _em.AddComponentData(e, new StreamStatus());
            _em.AddComponentData(e, new RecipeProcessData { RecipeID = recipeInput >= 0 ? 1 : -1 });
            var ri = _em.AddBuffer<RecipeInputSlot>(e);
            if (recipeInput >= 0) ri.Add(new RecipeInputSlot { ItemID = recipeInput, Quantity = 2 });
            return e;
        }

        StreamStatus Status(Entity e) => _em.GetComponentData<StreamStatus>(e);

        [Test]
        public void Flow_IsSharedUpstreamFirst()
        {
            Spring(10f);
            Stream();
            var a = Bank(1, 6f);
            var b = Bank(2, 6f);
            var c = Bank(3, 1f);

            Tick();

            Assert.AreEqual(1f, Status(a).Ratio, 1e-5f);
            Assert.AreEqual(4f / 6f, Status(b).Ratio, 1e-5f);
            Assert.AreEqual(0f, Status(c).Ratio, "the stream ran dry upstream");
            Assert.AreEqual(1, Status(c).OnStream);
        }

        [Test]
        public void BuildingAwayFromTheStream_DoesNotRun()
        {
            Spring(10f);
            Stream();
            var far = _em.CreateEntity();
            _em.AddComponentData(far, new BuildingData { IsActive = true });
            _em.AddComponentData(far, new GridPosition { Cell = new int2(1, 5) });
            _em.AddComponentData(far, new StreamConsumer { Draw = 1f });
            _em.AddComponentData(far, new StreamStatus { OnStream = 1, Ratio = 1f });

            Tick();

            Assert.AreEqual(0, Status(far).OnStream);
            Assert.AreEqual(0f, Status(far).Ratio);
        }

        [Test]
        public void NoSpring_StreamIsDry_ItemsStandStill()
        {
            var segs = Stream();
            ConveyorItems.Add(_em, segs[1], 5, 0.2f);

            Tick(0.1f);

            Assert.AreEqual(0, _em.GetComponentData<StreamSegment>(segs[1]).HasSource);
            Assert.AreEqual(0.2f, _em.GetBuffer<ConveyorItem>(segs[1])[0].Progress, 1e-5f);
        }

        [Test]
        public void BankBuilding_TakesOnlyWhatItsRecipeUses()
        {
            Spring(10f);
            var segs = Stream();
            var a = Bank(1, 3f, recipeInput: 5);
            ConveyorItems.Add(_em, segs[1], 9, 0.8f);   // unused: floats past
            ConveyorItems.Add(_em, segs[1], 5, 0.2f);   // used: picked off even though it is not in front

            Tick();

            Assert.AreEqual(1, SlotBufferUtils.CountInInputBuffer(_em.GetBuffer<BuildingInputSlot>(a), 5));
            Assert.AreEqual(0, SlotBufferUtils.CountInInputBuffer(_em.GetBuffer<BuildingInputSlot>(a), 9));
            var left = _em.GetBuffer<ConveyorItem>(segs[1]);
            Assert.AreEqual(1, left.Length);
            Assert.AreEqual(9, left[0].ItemID);
        }

        [Test]
        public void BankBuilding_WithNoRecipe_TakesNothing()
        {
            Spring(10f);
            var segs = Stream();
            var a = Bank(1, 3f);
            ConveyorItems.Add(_em, segs[1], 5, 0.2f);

            Tick();

            Assert.AreEqual(0, _em.GetBuffer<BuildingInputSlot>(a).Length);
        }

        [Test]
        public void BankBuilding_DropsItsProductIntoTheStream()
        {
            Spring(10f);
            var segs = Stream();
            var a = Bank(2, 3f);
            _em.GetBuffer<BuildingOutputSlot>(a).Add(new BuildingOutputSlot { ItemID = 11, Quantity = 1 });

            Tick();

            Assert.AreEqual(0, _em.GetBuffer<BuildingOutputSlot>(a).Length);
            var items = _em.GetBuffer<ConveyorItem>(segs[2]);
            Assert.AreEqual(1, items.Length);
            Assert.AreEqual(11, items[0].ItemID);
        }

        [Test]
        public void OpenMouth_SellsWhatReachesIt()
        {
            Spring(10f);
            var segs = Stream();
            ConveyorItems.Add(_em, segs[3], 5, 0.99f);

            Tick(0.1f); // reaches the end
            Tick(0.1f);

            Assert.AreEqual(0, _em.GetBuffer<ConveyorItem>(segs[3]).Length, "the mouth never holds items back");
        }
    }
}
