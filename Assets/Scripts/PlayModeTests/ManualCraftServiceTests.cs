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


        [SetUp]
        public void SetUp()
        {
            _savePath   = Path.Combine(Application.persistentDataPath, "save.json");
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
