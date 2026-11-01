using System.Collections.Generic;
using System.Reflection;
using System.IO;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder.PlayModeTests
{
    /// <summary>
    /// EditMode tests for ManualCraftService (CanCraft, TryCraft, GetInventoryCounts).
    ///
    /// ManualCraftService.CanCraft / TryCraft call ItemDatabase.Instance.GetItemId(string),
    /// so an ItemDatabase is required. The [SerializeField] items field is injected via
    /// reflection before Awake fires using the inactive-GameObject pattern established by
    /// AchievementServiceTests.
    ///
    /// ManualCraftService.Start() uses World.DefaultGameObjectInjectionWorld, which must be
    /// set before the service's Start() fires (done in SetUp). Start() is invoked explicitly
    /// via reflection since EditMode does not call it automatically.
    /// </summary>
    [TestFixture]
    public class ManualCraftServiceTests
    {
        string _savePath;
        string _saveBackup;

        GameObject _saveManagerGO;
        GameObject _itemDatabaseGO;
        GameObject _serviceGO;

        World         _testWorld;
        EntityManager _em;
        Entity        _playerEntity;

        ItemSO _hydrogenSO; // id="hydrogen", itemId=6
        ItemSO _protonSO;   // id="proton",   itemId=4
        BuildingSO _atomGenSO;


        [SetUp]
        public void SetUp()
        {
            _savePath   = Path.Combine(LocalSaveService.SaveDirectory, "save.json");
            _saveBackup = File.Exists(_savePath) ? File.ReadAllText(_savePath) : null;
            if (File.Exists(_savePath)) File.Delete(_savePath);

            _testWorld    = new World("ManualCraftTestWorld");
            _em           = _testWorld.EntityManager;
            _playerEntity = _em.CreateEntity();
            _em.AddComponent<PlayerInventoryTag>(_playerEntity);
            _em.AddBuffer<InventorySlot>(_playerEntity);

            World.DefaultGameObjectInjectionWorld = _testWorld;

            _hydrogenSO        = ScriptableObject.CreateInstance<ItemSO>();
            _hydrogenSO.id     = "hydrogen";
            _hydrogenSO.itemId = 6;

            _protonSO        = ScriptableObject.CreateInstance<ItemSO>();
            _protonSO.id     = "proton";
            _protonSO.itemId = 4;
        }

        [TearDown]
        public void TearDown()
        {
            if (_serviceGO      != null) { Object.DestroyImmediate(_serviceGO);      _serviceGO      = null; }
            if (_itemDatabaseGO != null) { Object.DestroyImmediate(_itemDatabaseGO); _itemDatabaseGO = null; }
            if (_saveManagerGO  != null) { Object.DestroyImmediate(_saveManagerGO);  _saveManagerGO  = null; }

            ManualCraftService.RecipeLookupOverride = null;
            ManualCraftService.BuildingsOverride    = null;
            ManualCraftService.QueueOverride        = null;
            if (_atomGenSO != null) { Object.DestroyImmediate(_atomGenSO); _atomGenSO = null; }

            if (_testWorld.IsCreated) _testWorld.Dispose();
            World.DefaultGameObjectInjectionWorld = null;

            if (File.Exists(_savePath)) File.Delete(_savePath);
            if (_saveBackup != null) File.WriteAllText(_savePath, _saveBackup);

            if (_hydrogenSO != null) { Object.DestroyImmediate(_hydrogenSO); _hydrogenSO = null; }
            if (_protonSO   != null) { Object.DestroyImmediate(_protonSO);   _protonSO   = null; }
        }

        void SpawnServices()
        {
            _saveManagerGO = new GameObject("SaveManager");
            { var sm = _saveManagerGO.AddComponent<SaveManager>(); RunAwake(sm); }

            _itemDatabaseGO = new GameObject("ItemDatabase");
            { var db = _itemDatabaseGO.AddComponent<ItemDatabase>(); RunAwake(db); } // Awake loads from Resources
            ItemDatabase.InjectForTesting(new ItemSO[] { _hydrogenSO, _protonSO }); // override with test SOs

            _serviceGO = new GameObject("ManualCraftService");
            var svc = _serviceGO.AddComponent<ManualCraftService>();
            RunAwake(svc);
            RunStart(svc); // Start() → ECS setup (_em, queries, IsReady = true)
        }

        static RecipeJson MakeRecipe(string inputId, int inputQty, string outputId, int outputQty) =>
            new RecipeJson
            {
                inputs = new List<RecipeIngredientJson> { new RecipeIngredientJson { id = inputId, quantity = inputQty } },
                output = new RecipeOutputJson { id = outputId, quantity = outputQty }
            };

        void AddInventoryItem(int itemId, int qty)
        {
            var buf = _em.GetBuffer<InventorySlot>(_playerEntity);
            SlotBufferUtils.AddToInventory(ref buf, itemId, qty);
        }

        // ── CanCraft ─────────────────────────────────────────────────────────

        [Test]
        public void CanCraft_FalseWhenInventoryEmpty()
        {
            SpawnServices();
            var recipe = MakeRecipe("hydrogen", 2, "proton", 1);
            Assert.IsFalse(ManualCraftService.Instance.CanCraft(recipe),
                "CanCraft must return false when inventory is empty");
        }

        [Test]
        public void CanCraft_TrueWhenInventoryHasSufficientItems()
        {
            SpawnServices();
            AddInventoryItem(_hydrogenSO.itemId, 3);
            var recipe = MakeRecipe("hydrogen", 2, "proton", 1);
            Assert.IsTrue(ManualCraftService.Instance.CanCraft(recipe),
                "CanCraft must return true when inventory has enough items");
        }

        // ── TryCraft ─────────────────────────────────────────────────────────

        [Test]
        public void TryCraft_ReturnsFalseWhenCannotCraft()
        {
            SpawnServices();
            var recipe = MakeRecipe("hydrogen", 2, "proton", 1);
            Assert.IsFalse(ManualCraftService.Instance.TryCraft(recipe),
                "TryCraft must return false when inputs are unavailable");
        }

        [Test]
        public void TryCraft_ConsumesInputItems()
        {
            SpawnServices();
            AddInventoryItem(_hydrogenSO.itemId, 3);
            var recipe = MakeRecipe("hydrogen", 2, "proton", 1);
            ManualCraftService.Instance.TryCraft(recipe);

            var buf = _em.GetBuffer<InventorySlot>(_playerEntity, isReadOnly: true);
            int remaining = SlotBufferUtils.CountInInventory(buf, _hydrogenSO.itemId);
            Assert.AreEqual(1, remaining, "TryCraft must consume the recipe's input quantity");
        }

        [Test]
        public void TryCraft_DepositsOutputItem()
        {
            SpawnServices();
            AddInventoryItem(_hydrogenSO.itemId, 2);
            var recipe = MakeRecipe("hydrogen", 2, "proton", 1);
            ManualCraftService.Instance.TryCraft(recipe);

            var buf = _em.GetBuffer<InventorySlot>(_playerEntity, isReadOnly: true);
            int produced = SlotBufferUtils.CountInInventory(buf, _protonSO.itemId);
            Assert.AreEqual(1, produced, "TryCraft must deposit the output item into the inventory");
        }

        // ── GetInventoryCounts ────────────────────────────────────────────────

        [Test]
        public void GetInventoryCounts_ReflectsCurrentBuffer()
        {
            SpawnServices();
            AddInventoryItem(_hydrogenSO.itemId, 5);
            AddInventoryItem(_protonSO.itemId, 3);

            var counts = ManualCraftService.Instance.GetInventoryCounts();
            Assert.AreEqual(5, counts[_hydrogenSO.itemId],
                "GetInventoryCounts must report correct quantity for hydrogen");
            Assert.AreEqual(3, counts[_protonSO.itemId],
                "GetInventoryCounts must report correct quantity for proton");
        }

        // ── Scene-reload re-bind (regression for the stale-world crafting bug) ──

        [Test]
        public void SceneReload_RebindsToNewWorld_InventoryVisibleAgain()
        {
            // ManualCraftService is a persistent (DontDestroyOnLoad) singleton, but the DOTS world
            // is rebuilt on every scene reload (e.g. starting a new game in-session). If it kept its
            // original EntityManager/query it would read a dead world — the HUD recipe panel then
            // shows 0/x and craft is greyed even though the inventory has the items.
            SpawnServices(); // binds to _testWorld (empty inventory)
            var recipe = MakeRecipe("hydrogen", 1, "proton", 1);
            Assert.IsFalse(ManualCraftService.Instance.CanCraft(recipe), "precondition: original world empty");

            var world2 = new World("ManualCraftTestWorld2");
            try
            {
                var em2     = world2.EntityManager;
                var player2 = em2.CreateEntity();
                em2.AddComponent<PlayerInventoryTag>(player2);
                var buf = em2.AddBuffer<InventorySlot>(player2);
                buf.Add(new InventorySlot { ItemID = _hydrogenSO.itemId, Quantity = 5 });
                World.DefaultGameObjectInjectionWorld = world2;

                // The fix: EnsureBound() on the next call notices the default world changed and re-acquires
                // the EntityManager + queries, so the recipe reads the rebuilt world's inventory.
                Assert.IsTrue(ManualCraftService.Instance.CanCraft(recipe),
                    "after the world is rebuilt the service must rebind and read the new world's inventory");
            }
            finally
            {
                World.DefaultGameObjectInjectionWorld = _testWorld; // TearDown disposes _testWorld
                if (world2.IsCreated) world2.Dispose();
            }
        }

        // ── Craft queue (#310) ────────────────────────────────────────────────

        RecipeJson SetUpQueue(RecipeJson recipe)
        {
            SpawnServices();
            ManualCraftService.QueueOverride        = new CraftQueueSaveData();
            ManualCraftService.RecipeLookupOverride = id => id == recipe.id ? recipe : null;
            return recipe;
        }

        static RecipeJson MenuRecipe(string id, string inputId, int inputQty, string outputId, float seconds)
        {
            var r = MakeRecipe(inputId, inputQty, outputId, 1);
            r.id = id;
            r.base_craft_time = seconds;
            r.crafted_in = new List<string> { ManualCraftService.InventoryMenuSource };
            return r;
        }

        int Count(ItemSO item) =>
            SlotBufferUtils.CountInInventory(_em.GetBuffer<InventorySlot>(_playerEntity, isReadOnly: true), item.itemId);

        [Test]
        public void MaxCraftable_IsTheFewestWholeCraftsAnyInputAllows()
        {
            SpawnServices();
            AddInventoryItem(_hydrogenSO.itemId, 7);
            Assert.AreEqual(3, ManualCraftService.Instance.MaxCraftable(MakeRecipe("hydrogen", 2, "proton", 1)));
        }

        [Test]
        public void TryEnqueue_TakesAllInputsNow_AndDepositsOnlyWhenEachItemFinishes()
        {
            var recipe = SetUpQueue(MenuRecipe("proton", "hydrogen", 2, "proton", 1f));
            AddInventoryItem(_hydrogenSO.itemId, 10);

            Assert.AreEqual(ManualCraftService.EnqueueResult.Queued,
                ManualCraftService.Instance.TryEnqueue(recipe, 5, out _));
            Assert.AreEqual(0, Count(_hydrogenSO), "all five crafts' inputs are taken when queued");
            Assert.AreEqual(0, Count(_protonSO), "nothing is made instantly");

            // Enqueue stamped the queue with GameClock.UtcNow; tick relative to that stamp.
            var start = System.DateTime.Parse(ManualCraftService.QueueOverride.activeCompleteUtc, null,
                System.Globalization.DateTimeStyles.RoundtripKind).AddSeconds(-1);
            ManualCraftService.Instance.TickQueue(start.AddSeconds(2.5));
            Assert.AreEqual(2, Count(_protonSO), "one proton per second of craft time");

            ManualCraftService.Instance.TickQueue(start.AddSeconds(60));
            Assert.AreEqual(5, Count(_protonSO));
            Assert.IsTrue(CraftQueue.IsEmpty(ManualCraftService.QueueOverride));
        }

        [Test]
        public void TryEnqueue_MoreThanTheInventoryCanPayFor_IsRefusedAndTakesNothing()
        {
            var recipe = SetUpQueue(MenuRecipe("proton", "hydrogen", 2, "proton", 1f));
            AddInventoryItem(_hydrogenSO.itemId, 9);

            Assert.AreEqual(ManualCraftService.EnqueueResult.NotEnoughMaterials,
                ManualCraftService.Instance.TryEnqueue(recipe, 5, out _));
            Assert.AreEqual(9, Count(_hydrogenSO));
            Assert.IsTrue(CraftQueue.IsEmpty(ManualCraftService.QueueOverride));
        }

        [Test]
        public void CancelQueued_RefundsTheInputsOfEveryItemLeft()
        {
            var recipe = SetUpQueue(MenuRecipe("proton", "hydrogen", 2, "proton", 1f));
            AddInventoryItem(_hydrogenSO.itemId, 10);
            ManualCraftService.Instance.TryEnqueue(recipe, 5, out _);

            ManualCraftService.Instance.CancelQueued(0);
            Assert.AreEqual(10, Count(_hydrogenSO));
            Assert.AreEqual(0, Count(_protonSO));
            Assert.IsTrue(CraftQueue.IsEmpty(ManualCraftService.QueueOverride));
        }

        // ── Building-only recipes (#309) ──────────────────────────────────────

        RecipeJson AtomGenRecipe()
        {
            _atomGenSO = ScriptableObject.CreateInstance<BuildingSO>();
            _atomGenSO.id          = "atomic_assembler";
            _atomGenSO.buildingId  = 4;
            _atomGenSO.displayName = "Atom Generator";
            ManualCraftService.BuildingsOverride = new[] { _atomGenSO };

            // Stands in for Helium-4: made only in the Atom Generator.
            var r = MakeRecipe("hydrogen", 1, "proton", 1);
            r.id = "helium_4";
            r.base_craft_time = 4f;
            r.crafted_in = new List<string> { "atomic_assembler" };
            return SetUpQueue(r);
        }

        void PlaceAtomGenerator(float speed, int currentRecipeOutputItemId)
        {
            var e = _em.CreateEntity();
            _em.AddComponentData(e, new BuildingData { BuildingType = 4, ProductionSpeed = speed, IsActive = true });
            // Busy making something else: the old path refused any building not already set to this
            // recipe or mid-cycle, which is what showed "No eligible building".
            _em.AddComponentData(e, new RecipeProcessData { IsCrafting = true, CraftTime = 2f });
            _em.AddBuffer<RecipeOutputSlot>(e).Add(new RecipeOutputSlot { ItemID = currentRecipeOutputItemId, Quantity = 1 });
        }

        [Test]
        public void BuildingRecipe_AtomGeneratorSetToAnotherRecipe_CanStillCraftFromTheMenu()
        {
            var recipe = AtomGenRecipe();
            PlaceAtomGenerator(speed: 2f, currentRecipeOutputItemId: _hydrogenSO.itemId);
            AddInventoryItem(_hydrogenSO.itemId, 3);

            Assert.IsTrue(ManualCraftService.Instance.HasCraftingBuilding(recipe, out float speed, out _));
            Assert.AreEqual(2f, speed);
            Assert.AreEqual(ManualCraftService.EnqueueResult.Queued,
                ManualCraftService.Instance.TryEnqueue(recipe, 3, out _));
            Assert.AreEqual(2f, ManualCraftService.QueueOverride.entries[0].unitSeconds, 1e-4f,
                "craft time is the recipe's 4s at the generator's 2x speed");
        }

        [Test]
        public void BuildingRecipe_NoBuildingPlaced_NamesTheBuildingToPlace()
        {
            var recipe = AtomGenRecipe();
            AddInventoryItem(_hydrogenSO.itemId, 3);

            Assert.AreEqual(ManualCraftService.EnqueueResult.NoBuilding,
                ManualCraftService.Instance.TryEnqueue(recipe, 1, out string missing));
            Assert.AreEqual("Atom Generator", missing);
            Assert.AreEqual(3, Count(_hydrogenSO), "a refused craft takes nothing");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        static void RunStart(MonoBehaviour mb) =>
            mb.GetType()
              .GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
              ?.Invoke(mb, null);

        static void RunAwake(MonoBehaviour mb)
        {
            var t = mb.GetType();
            while (t != null && t != typeof(MonoBehaviour))
            {
                var m = t.GetMethod("Awake",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (m != null) { m.Invoke(mb, null); return; }
                t = t.BaseType;
            }
        }
    }
}
