using NUnit.Framework;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Edit Mode ECS tests for ConveyorSystem's belt -> building-input handoff.
    ///
    /// Each test builds an isolated World with a single building (carrying one Input port) and a
    /// tail conveyor segment carrying a ready-to-deposit item, then ticks once and checks whether
    /// the item entered the building's input buffer. The key behaviour under test: a belt must feed
    /// an input whenever its tail sits in the cell in front of that input, regardless of the belt's
    /// own exit direction.
    /// </summary>
    [TestFixture]
    public class ConveyorHandoffTests
    {
        private World         _world;
        private EntityManager _em;

        // OutputDirection: North=0, East=1, South=2, West=3
        private const int North = 0;
        private const int East  = 1;
        private const int West  = 3;

        private const int ItemId = 7;

        [SetUp]
        public void SetUp()
        {
            _world = new World("ConveyorHandoffTest");
            _em    = _world.EntityManager;

            var simGroup = _world.CreateSystemManaged<SimulationSystemGroup>();
            simGroup.AddSystemToUpdateList(_world.CreateSystem<ConveyorSystem>());
            simGroup.SortSystems();
        }

        [TearDown]
        public void TearDown()
        {
            if (_world.IsCreated) _world.Dispose();
        }

        private void Tick(float deltaTime = 1f)
        {
            _world.SetTime(new TimeData(elapsedTime: 0, deltaTime: deltaTime));
            _world.Update();
        }

        /// <summary>Building at <paramref name="cell"/> with a single Input port facing <paramref name="inputFacing"/>.</summary>
        private Entity CreateBuildingWithInput(int2 cell, int inputFacing, int inputCapacity = 10)
        {
            var e = _em.CreateEntity();
            _em.AddComponentData(e, new BuildingData { IsActive = true, ProductionSpeed = 1f });
            _em.AddComponentData(e, new GridPosition { Cell = cell });

            var ports = _em.AddBuffer<PlacedPortData>(e);
            ports.Add(new PlacedPortData
            {
                PortType = (int)PortType.Input,
                CellX    = cell.x,
                CellY    = cell.y,
                Facing   = inputFacing
            });

            _em.AddBuffer<BuildingInputSlot>(e);
            _em.AddBuffer<BuildingOutputSlot>(e);
            _em.AddComponentData(e, new BuildingInventoryConfig
            {
                OutputCapacity = 10,
                InputCapacity  = inputCapacity
            });
            return e;
        }

        /// <summary>A loaded tail segment at <paramref name="cell"/> exiting toward <paramref name="exitDir"/>.</summary>
        private Entity CreateLoadedTail(int2 cell, int exitDir)
        {
            var e = _em.CreateEntity();
            _em.AddComponentData(e, new ConveyorSegmentData
            {
                Cell          = cell,
                EntryDir      = exitDir,
                ExitDir       = exitDir,
                NextSegment   = Entity.Null,
                PrevSegment   = Entity.Null,
                TransportTime = 1f,
                IsChainHead   = true
            });
            _em.AddComponentData(e, new ConveyorItemData { ItemID = ItemId, Progress = 1f });
            return e;
        }

        private int InputTotal(Entity building) =>
            SlotBufferUtils.TotalInInputBuffer(_em.GetBuffer<BuildingInputSlot>(building));

        // ── Happy path: perpendicular belt still feeds the input ─────────────

        [Test]
        public void PerpendicularTail_InFrontOfInput_DepositsItem()
        {
            // Building at (1,0) with an input that accepts from the west (Facing = East = flow into
            // the building). The belt tail sits at (0,0) — the cell in front of that input — but
            // travels NORTH, perpendicular to the input direction.
            var building = CreateBuildingWithInput(new int2(1, 0), East);
            var tail     = CreateLoadedTail(new int2(0, 0), North);

            Tick();

            Assert.AreEqual(1, InputTotal(building), "Item must enter the input even when the belt runs perpendicular to it");
            Assert.IsFalse(_em.HasComponent<ConveyorItemData>(tail), "Item must be removed from the belt after handoff");
        }

        // ── Backward compatibility: belt exit points straight into the input ──

        [Test]
        public void TailExitingIntoInput_DepositsItem()
        {
            var building = CreateBuildingWithInput(new int2(1, 0), East);
            var tail     = CreateLoadedTail(new int2(0, 0), East); // exit points right at the building

            Tick();

            Assert.AreEqual(1, InputTotal(building), "Belt exiting straight into the input must still deposit");
            Assert.IsFalse(_em.HasComponent<ConveyorItemData>(tail));
        }

        // ── Negative: input faces away from the belt ─────────────────────────

        [Test]
        public void InputFacingAwayFromBelt_DoesNotDeposit()
        {
            // Input at (1,0) faces West — its front cell is to the EAST at (2,0), not where the
            // belt sits. The belt at (0,0) is behind the input and must not feed it.
            var building = CreateBuildingWithInput(new int2(1, 0), West);
            var tail     = CreateLoadedTail(new int2(0, 0), East);

            Tick();

            Assert.AreEqual(0, InputTotal(building), "An input that does not face the belt must not receive the item");
            Assert.IsTrue(_em.HasComponent<ConveyorItemData>(tail), "Item must remain on the belt when no input faces it");
        }

        // ── Back-pressure: full input keeps the item on the belt ─────────────

        [Test]
        public void InputAtCapacity_ItemWaitsOnBelt()
        {
            var building = CreateBuildingWithInput(new int2(1, 0), East, inputCapacity: 1);
            SlotBufferUtils.AddToInputBuffer(_em.GetBuffer<BuildingInputSlot>(building), ItemId, 1); // fill it
            var tail = CreateLoadedTail(new int2(0, 0), North);

            Tick();

            Assert.AreEqual(1, InputTotal(building), "A full input must not accept more");
            Assert.IsTrue(_em.HasComponent<ConveyorItemData>(tail), "Item must wait on the belt while the input is full");
        }
    }
}
