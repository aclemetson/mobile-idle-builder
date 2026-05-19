using NUnit.Framework;
using Unity.Core;
using Unity.Entities;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Edit Mode ECS tests for ProductionSystem.
    ///
    /// Each test creates an isolated World with a player inventory entity and a building
    /// entity wired with recipe components, then drives time and ticks to verify the
    /// production loop: input check → IsCrafting → progress → completion → output deposit.
    /// </summary>
    [TestFixture]
    public class ProductionSystemTests
    {
        private World         _world;
        private EntityManager _em;
        private Entity        _playerEntity;
        private Entity        _buildingEntity;

        // Shared item/recipe IDs used across tests
        private const int InputItemId  = 1;
        private const int OutputItemId = 4;
        private const int InputQty     = 2;
        private const float CraftTime  = 2f;
        private const float Speed      = 1f;

        [SetUp]
        public void SetUp()
        {
            _world = new World("ProductionTest");
            _em    = _world.EntityManager;

            var simGroup = _world.CreateSystemManaged<SimulationSystemGroup>();
            simGroup.AddSystemToUpdateList(_world.CreateSystem<ProductionSystem>());
            simGroup.SortSystems();

            // Player entity — holds the global InventorySlot singleton
            _playerEntity = _em.CreateEntity();
            _em.AddComponent<PlayerInventoryTag>(_playerEntity);
            _em.AddBuffer<InventorySlot>(_playerEntity);

            // Building entity
            _buildingEntity = _em.CreateEntity();
            _em.AddComponentData(_buildingEntity, new BuildingData
            {
                IsActive        = true,
                ProductionSpeed = Speed
            });
            _em.AddComponentData(_buildingEntity, new RecipeProcessData
            {
                RecipeID  = 1,
                CraftTime = CraftTime,
                Progress  = 0f,
                IsCrafting = false
            });
            var inputBuf = _em.AddBuffer<RecipeInputSlot>(_buildingEntity);
            inputBuf.Add(new RecipeInputSlot { ItemID = InputItemId, Quantity = InputQty });
            var outputBuf = _em.AddBuffer<RecipeOutputSlot>(_buildingEntity);
            outputBuf.Add(new RecipeOutputSlot { ItemID = OutputItemId, Quantity = 1 });
            _em.AddBuffer<BuildingInputSlot>(_buildingEntity);
            _em.AddBuffer<BuildingOutputSlot>(_buildingEntity);
            _em.AddComponentData(_buildingEntity, new BuildingInventoryConfig
            {
                OutputCapacity = 20,
                InputCapacity  = 20
            });
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

        private void AddToGlobalInventory(int itemId, int qty)
        {
            var buf = _em.GetBuffer<InventorySlot>(_playerEntity);
            SlotBufferUtils.AddToInventory(ref buf, itemId, qty);
        }

        // ── Inactive building ────────────────────────────────────────────────

        [Test]
        public void WhenBuildingInactive_NoProgressMade()
        {
            _em.SetComponentData(_buildingEntity, new BuildingData { IsActive = false, ProductionSpeed = Speed });
            AddToGlobalInventory(InputItemId, 10);

            Tick();

            var process = _em.GetComponentData<RecipeProcessData>(_buildingEntity);
            Assert.AreEqual(0f,    process.Progress,    "Inactive building must not accumulate progress");
            Assert.IsFalse(process.IsCrafting,          "Inactive building must not start crafting");
        }

        // ── Input availability ───────────────────────────────────────────────

        [Test]
        public void WhenNoInputsAvailable_CraftingDoesNotStart()
        {
            // Global inventory empty, local input buffer empty — should not start

            Tick();

            var process = _em.GetComponentData<RecipeProcessData>(_buildingEntity);
            Assert.IsFalse(process.IsCrafting, "No inputs → crafting must not start");
        }

        [Test]
        public void WhenGlobalInventoryHasInputs_CraftingStarts()
        {
            AddToGlobalInventory(InputItemId, InputQty);

            Tick();

            var process = _em.GetComponentData<RecipeProcessData>(_buildingEntity);
            Assert.IsTrue(process.IsCrafting, "Sufficient global inventory → crafting must start");
        }

        [Test]
        public void WhenLocalInputBufferHasItems_LocalTakesPriorityOverGlobal()
        {
            // Global inventory empty; local input buffer populated
            var localIn = _em.GetBuffer<BuildingInputSlot>(_buildingEntity);
            SlotBufferUtils.AddToInputBuffer(localIn, InputItemId, InputQty);

            Tick();

            var process = _em.GetComponentData<RecipeProcessData>(_buildingEntity);
            Assert.IsTrue(process.IsCrafting, "Local input buffer populated → crafting must start even without global inventory");
        }

        // ── Progress ─────────────────────────────────────────────────────────

        [Test]
        public void WhenCrafting_ProgressAdvancesWithDeltaTime()
        {
            AddToGlobalInventory(InputItemId, InputQty);
            _em.SetComponentData(_buildingEntity, new RecipeProcessData
            {
                RecipeID   = 1,
                CraftTime  = CraftTime,
                IsCrafting = true,
                Progress   = 0f
            });

            Tick(deltaTime: 0.5f);

            var process = _em.GetComponentData<RecipeProcessData>(_buildingEntity);
            Assert.AreEqual(0.5f * Speed, process.Progress, 0.001f, "Progress must advance by deltaTime × ProductionSpeed");
        }

        // ── Completion ────────────────────────────────────────────────────────

        [Test]
        public void WhenProgressMeetsCraftTime_ConsumesInputsFromGlobalInventory()
        {
            AddToGlobalInventory(InputItemId, InputQty * 2); // extra to verify only one cycle consumed
            _em.SetComponentData(_buildingEntity, new RecipeProcessData
            {
                RecipeID   = 1,
                CraftTime  = 0.1f, // very short so one tick completes it
                IsCrafting = true,
                Progress   = 0.09f
            });

            Tick(deltaTime: 1f);

            var inv = _em.GetBuffer<InventorySlot>(_playerEntity);
            int remaining = SlotBufferUtils.CountInInventory(inv, InputItemId);
            Assert.AreEqual(InputQty, remaining,
                $"One craft cycle must consume {InputQty} inputs; started with {InputQty * 2}");
        }

        [Test]
        public void WhenProgressMeetsCraftTime_DepositsOutputToLocalBuffer()
        {
            AddToGlobalInventory(InputItemId, InputQty);
            _em.SetComponentData(_buildingEntity, new RecipeProcessData
            {
                RecipeID   = 1,
                CraftTime  = 0.1f,
                IsCrafting = true,
                Progress   = 0.09f
            });

            Tick(deltaTime: 1f);

            var outputBuf = _em.GetBuffer<BuildingOutputSlot>(_buildingEntity);
            int produced = SlotBufferUtils.CountInOutputBuffer(outputBuf, OutputItemId);
            Assert.AreEqual(1, produced, "Completed craft must deposit 1 output item to BuildingOutputSlot");
        }

        [Test]
        public void WhenCraftCompletes_ProgressResetsToZero()
        {
            AddToGlobalInventory(InputItemId, InputQty);
            _em.SetComponentData(_buildingEntity, new RecipeProcessData
            {
                RecipeID   = 1,
                CraftTime  = 0.1f,
                IsCrafting = true,
                Progress   = 0.09f
            });

            Tick(deltaTime: 1f);

            var process = _em.GetComponentData<RecipeProcessData>(_buildingEntity);
            Assert.AreEqual(0f, process.Progress, 0.001f, "Progress must reset to 0 after completion");
        }

        // ── Output capacity ───────────────────────────────────────────────────

        [Test]
        public void WhenOutputBufferAtCapacity_ProductionPauses()
        {
            AddToGlobalInventory(InputItemId, InputQty);
            _em.SetComponentData(_buildingEntity, new RecipeProcessData
            {
                RecipeID   = 1,
                CraftTime  = CraftTime,
                IsCrafting = true,
                Progress   = CraftTime - 0.01f // almost done
            });

            // Fill output buffer to capacity
            var config = _em.GetComponentData<BuildingInventoryConfig>(_buildingEntity);
            var outBuf = _em.GetBuffer<BuildingOutputSlot>(_buildingEntity);
            outBuf.Add(new BuildingOutputSlot { ItemID = OutputItemId, Quantity = config.OutputCapacity });

            Tick(deltaTime: 1f);

            var process = _em.GetComponentData<RecipeProcessData>(_buildingEntity);
            Assert.IsFalse(process.IsCrafting, "Full output buffer must pause production");
        }
    }
}
