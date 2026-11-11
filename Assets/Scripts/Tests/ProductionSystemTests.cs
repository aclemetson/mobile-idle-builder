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

        // ── Power gating ──────────────────────────────────────────────────────

        [Test]
        public void WhenDisconnectedFromPower_CraftingDoesNotStart()
        {
            _em.AddComponentData(_buildingEntity, new PowerStatus { IsConnected = 0, ThrottleRatio = 0f });
            AddToGlobalInventory(InputItemId, InputQty);

            Tick();

            var process = _em.GetComponentData<RecipeProcessData>(_buildingEntity);
            Assert.IsFalse(process.IsCrafting, "Unpowered building must not start crafting");
            Assert.AreEqual(0f, process.Progress, 0.001f, "Unpowered building must not accumulate progress");
        }

        [Test]
        public void WhenBrownout_ProgressAdvancesAtThrottledRate()
        {
            _em.AddComponentData(_buildingEntity, new PowerStatus { IsConnected = 1, ThrottleRatio = 0.5f });
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
            Assert.AreEqual(0.5f * Speed * 0.5f, process.Progress, 0.001f,
                "Brownout must scale progress by ThrottleRatio");
        }

        // ── Escaping byproducts (fusion neutrinos / gamma) ───────────────────

        private Entity AddProgress(long currency)
        {
            var e = _em.CreateEntity();
            _em.AddComponentData(e, new PlayerProgressData { BaseCurrency = currency });
            return e;
        }

        private void SetByproductEntropy(float perCraft)
        {
            var p = _em.GetComponentData<RecipeProcessData>(_buildingEntity);
            p.ByproductEntropy = perCraft;
            _em.SetComponentData(_buildingEntity, p);
        }

        [Test]
        public void CompletedCraft_PaysByproductEntropy()
        {
            var progress = AddProgress(100);
            SetByproductEntropy(5f);
            AddToGlobalInventory(InputItemId, InputQty);

            Tick(CraftTime);

            Assert.AreEqual(105, _em.GetComponentData<PlayerProgressData>(progress).BaseCurrency,
                "A finished craft pays its escaping byproducts' entropy");
        }

        [Test]
        public void CompletedCraft_CountsByproductEntropyAsAutomationIncome()
        {
            AddProgress(100);
            var incomeEntity = _em.CreateEntity();
            _em.AddComponentData(incomeEntity, new AutomationIncome { Total = 0 });
            SetByproductEntropy(5f);
            AddToGlobalInventory(InputItemId, InputQty);

            Tick(CraftTime);

            Assert.AreEqual(5.0, _em.GetComponentData<AutomationIncome>(incomeEntity).Total, 0.0001,
                "Escaping byproduct entropy counts toward automation income");
        }

        [Test]
        public void UnfinishedCraft_PaysNoByproductEntropy()
        {
            var progress = AddProgress(100);
            SetByproductEntropy(5f);
            AddToGlobalInventory(InputItemId, InputQty);

            Tick(CraftTime * 0.5f);

            Assert.AreEqual(100, _em.GetComponentData<PlayerProgressData>(progress).BaseCurrency);
        }

        [Test]
        public void ByproductEntropy_ScalesWithGlobalOutputBonus()
        {
            var progress = AddProgress(0);
            var bonus = _em.CreateEntity();
            _em.AddComponentData(bonus, new GlobalProductionBonus { SpeedMult = 1f, OutputMult = 2f });
            SetByproductEntropy(5f);
            AddToGlobalInventory(InputItemId, InputQty);

            Tick(CraftTime);

            Assert.AreEqual(10, _em.GetComponentData<PlayerProgressData>(progress).BaseCurrency);
        }
    
        // ── Prestige shop multipliers (Assembly Line / Quantum Yield are live) ──

        private void AddPrestige(float speed, float output)
        {
            _em.AddComponentData(_playerEntity, new PrestigeData { SpeedMultiplier = speed, OutputMultiplier = output });
        }

        private int OutputCount() =>
            SlotBufferUtils.CountInOutputBuffer(_em.GetBuffer<BuildingOutputSlot>(_buildingEntity), OutputItemId);

        [Test]
        public void PrestigeSpeedMultiplier_AdvancesCraftFaster()
        {
            AddPrestige(speed: 1.5f, output: 1f);
            AddToGlobalInventory(InputItemId, InputQty);
            _em.SetComponentData(_buildingEntity, new RecipeProcessData
            {
                RecipeID = 1, CraftTime = CraftTime, IsCrafting = true, Progress = 0f
            });

            Tick(deltaTime: 0.5f);

            Assert.AreEqual(0.75f, _em.GetComponentData<RecipeProcessData>(_buildingEntity).Progress, 1e-4f,
                "Assembly Line speed must scale live craft progress");
        }

        [Test]
        public void PrestigeOutputMultiplier_PaysTheFractionOverManyCrafts()
        {
            // 1.5x on a 1-item recipe used to floor to 1 every craft; the carry pays 1, 2, 1, 2.
            AddPrestige(speed: 1f, output: 1.5f);
            for (int craft = 0; craft < 4; craft++)
            {
                AddToGlobalInventory(InputItemId, InputQty);
                Tick(deltaTime: CraftTime); // auto-starts and completes in one tick
            }
            Assert.AreEqual(6, OutputCount(), "4 crafts x 1.5 = 6 items");
        }

        [Test]
        public void NoPrestigeSingleton_BehavesAsBefore()
        {
            AddToGlobalInventory(InputItemId, InputQty);
            Tick(deltaTime: CraftTime);
            Assert.AreEqual(1, OutputCount());
        }
    }
}
