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

        // ── #200: per-item cap, recipe filter, jam overflow ──────────────────

        private const int UpQuark   = 1;
        private const int DownQuark = 2;

        /// <summary>Gives the building a proton-like recipe: 2 up + 1 down.</summary>
        private void SetProtonRecipe(Entity building)
        {
            _em.AddComponentData(building, new RecipeProcessData { RecipeID = 4, CraftTime = 1f });
            var inputs = _em.AddBuffer<RecipeInputSlot>(building);
            inputs.Add(new RecipeInputSlot { ItemID = UpQuark,   Quantity = 2 });
            inputs.Add(new RecipeInputSlot { ItemID = DownQuark, Quantity = 1 });
        }

        private Entity CreateTail(int2 cell, int exitDir, int itemId, Entity prev)
        {
            var e = CreateLoadedTail(cell, exitDir);
            _em.SetComponentData(e, new ConveyorItemData { ItemID = itemId, Progress = 1f });
            var seg = _em.GetComponentData<ConveyorSegmentData>(e);
            seg.PrevSegment = prev;
            seg.IsChainHead = prev == Entity.Null;
            _em.SetComponentData(e, seg);
            return e;
        }

        /// <summary>Upstream segment carrying <paramref name="itemId"/>, mid-belt (not ready to move).</summary>
        private Entity CreateUpstream(int2 cell, int itemId)
        {
            var e = _em.CreateEntity();
            _em.AddComponentData(e, new ConveyorSegmentData
            {
                Cell = cell, EntryDir = East, ExitDir = East, TransportTime = 1f, IsChainHead = true
            });
            _em.AddComponentData(e, new ConveyorItemData { ItemID = itemId, Progress = 0.5f });
            return e;
        }

        private Entity CreatePlayerInventory()
        {
            var e = _em.CreateEntity();
            _em.AddComponent<PlayerInventoryTag>(e);
            _em.AddBuffer<InventorySlot>(e);
            return e;
        }

        private int CountIn(Entity building, int itemId) =>
            SlotBufferUtils.CountInInputBuffer(_em.GetBuffer<BuildingInputSlot>(building), itemId);

        [Test]
        public void CapIsPerItem_FullOfOneItem_StillAcceptsAnother()
        {
            // Regression for #200: 30 down quarks used to fill the whole (total) buffer and lock the
            // up-quark belt out, stalling the Combiner.
            var building = CreateBuildingWithInput(new int2(1, 0), East, inputCapacity: 30);
            SetProtonRecipe(building);
            SlotBufferUtils.AddToInputBuffer(_em.GetBuffer<BuildingInputSlot>(building), DownQuark, 30);
            var tail = CreateTail(new int2(0, 0), East, UpQuark, Entity.Null);

            Tick();

            Assert.AreEqual(1, CountIn(building, UpQuark), "An up quark must enter even though downs are at their cap");
            Assert.IsFalse(_em.HasComponent<ConveyorItemData>(tail));
        }

        [Test]
        public void RecipeSet_UnusedItem_WaitsOnBelt()
        {
            var building = CreateBuildingWithInput(new int2(1, 0), East, inputCapacity: 30);
            SetProtonRecipe(building);
            var tail = CreateTail(new int2(0, 0), East, ItemId, Entity.Null); // ItemId 7: not in the recipe

            Tick();

            Assert.AreEqual(0, InputTotal(building), "The recipe does not use this item");
            Assert.IsTrue(_em.HasComponent<ConveyorItemData>(tail), "Unused item stays on the belt");
        }

        [Test]
        public void MixedBelt_FullItemJammingNeededItem_OverflowsToInventory()
        {
            var building = CreateBuildingWithInput(new int2(1, 0), East, inputCapacity: 30);
            SetProtonRecipe(building);
            SlotBufferUtils.AddToInputBuffer(_em.GetBuffer<BuildingInputSlot>(building), DownQuark, 30);
            var inventory = CreatePlayerInventory();

            var upstream = CreateUpstream(new int2(-1, 0), UpQuark);          // the needed item, stuck behind
            var tail     = CreateTail(new int2(0, 0), East, DownQuark, upstream); // full item at the front

            Tick();

            Assert.IsFalse(_em.HasComponent<ConveyorItemData>(tail), "Jamming item must leave the belt");
            Assert.AreEqual(30, CountIn(building, DownQuark), "Building buffer stays at its cap");
            Assert.AreEqual(1, SlotBufferUtils.CountInInventory(_em.GetBuffer<InventorySlot>(inventory), DownQuark),
                "Jamming item goes to the player's inventory, not lost");
        }

        [Test]
        public void SingleItemBelt_AtCap_BacksUp_NoOverflow()
        {
            // Separate belts per input must keep normal back-pressure: nothing needed is behind.
            var building = CreateBuildingWithInput(new int2(1, 0), East, inputCapacity: 30);
            SetProtonRecipe(building);
            SlotBufferUtils.AddToInputBuffer(_em.GetBuffer<BuildingInputSlot>(building), DownQuark, 30);
            var inventory = CreatePlayerInventory();

            var upstream = CreateUpstream(new int2(-1, 0), DownQuark);
            var tail     = CreateTail(new int2(0, 0), East, DownQuark, upstream);

            Tick();

            Assert.IsTrue(_em.HasComponent<ConveyorItemData>(tail), "Same-item belt just backs up");
            Assert.AreEqual(0, SlotBufferUtils.CountInInventory(_em.GetBuffer<InventorySlot>(inventory), DownQuark));
        }
    }
}
