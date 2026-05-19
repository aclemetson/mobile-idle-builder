using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;

namespace MobileIdleBuilder.PlayModeTests
{
    /// <summary>
    /// PlayMode tests for ManualCraftService (CanCraft, TryCraft, GetInventoryCounts).
    ///
    /// ManualCraftService.CanCraft / TryCraft call ItemDatabase.Instance.GetItemId(string),
    /// so an ItemDatabase is required. The [SerializeField] items field is injected via
    /// reflection before Awake fires using the inactive-GameObject pattern established by
    /// AchievementServiceTests.
    ///
    /// ManualCraftService.Start() uses World.DefaultGameObjectInjectionWorld, which must be
    /// set before the service's Start() fires (done in SetUp).
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

        static readonly FieldInfo s_itemsField =
            typeof(ItemDatabase).GetField("items", BindingFlags.NonPublic | BindingFlags.Instance);

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

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_serviceGO      != null) { Object.Destroy(_serviceGO);      _serviceGO      = null; }
            if (_itemDatabaseGO != null) { Object.Destroy(_itemDatabaseGO); _itemDatabaseGO = null; }
            if (_saveManagerGO  != null) { Object.Destroy(_saveManagerGO);  _saveManagerGO  = null; }
            yield return null;

            if (_testWorld.IsCreated) _testWorld.Dispose();
            World.DefaultGameObjectInjectionWorld = null;

            if (File.Exists(_savePath)) File.Delete(_savePath);
            if (_saveBackup != null) File.WriteAllText(_savePath, _saveBackup);

            if (_hydrogenSO != null) { Object.Destroy(_hydrogenSO); _hydrogenSO = null; }
            if (_protonSO   != null) { Object.Destroy(_protonSO);   _protonSO   = null; }
        }

        IEnumerator SpawnServices()
        {
            _saveManagerGO = new GameObject("SaveManager");
            _saveManagerGO.AddComponent<SaveManager>();
            yield return null;

            // Inject items before Awake fires via the inactive-GO pattern
            var dbGO = new GameObject("ItemDatabase");
            dbGO.SetActive(false);
            var itemDb = dbGO.AddComponent<ItemDatabase>();
            s_itemsField.SetValue(itemDb, new ItemSO[] { _hydrogenSO, _protonSO });
            dbGO.SetActive(true); // triggers Awake, builds static lookup
            _itemDatabaseGO = dbGO;
            yield return null;

            _serviceGO = new GameObject("ManualCraftService");
            _serviceGO.AddComponent<ManualCraftService>();
            yield return null;
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

        [UnityTest]
        public IEnumerator CanCraft_FalseWhenInventoryEmpty()
        {
            yield return SpawnServices();
            var recipe = MakeRecipe("hydrogen", 2, "proton", 1);
            Assert.IsFalse(ManualCraftService.Instance.CanCraft(recipe),
                "CanCraft must return false when inventory is empty");
        }

        [UnityTest]
        public IEnumerator CanCraft_TrueWhenInventoryHasSufficientItems()
        {
            yield return SpawnServices();
            AddInventoryItem(_hydrogenSO.itemId, 3);
            var recipe = MakeRecipe("hydrogen", 2, "proton", 1);
            Assert.IsTrue(ManualCraftService.Instance.CanCraft(recipe),
                "CanCraft must return true when inventory has enough items");
        }

        // ── TryCraft ─────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator TryCraft_ReturnsFalseWhenCannotCraft()
        {
            yield return SpawnServices();
            var recipe = MakeRecipe("hydrogen", 2, "proton", 1);
            Assert.IsFalse(ManualCraftService.Instance.TryCraft(recipe),
                "TryCraft must return false when inputs are unavailable");
        }

        [UnityTest]
        public IEnumerator TryCraft_ConsumesInputItems()
        {
            yield return SpawnServices();
            AddInventoryItem(_hydrogenSO.itemId, 3);
            var recipe = MakeRecipe("hydrogen", 2, "proton", 1);
            ManualCraftService.Instance.TryCraft(recipe);

            var buf = _em.GetBuffer<InventorySlot>(_playerEntity, isReadOnly: true);
            int remaining = SlotBufferUtils.CountInInventory(buf, _hydrogenSO.itemId);
            Assert.AreEqual(1, remaining, "TryCraft must consume the recipe's input quantity");
        }

        [UnityTest]
        public IEnumerator TryCraft_DepositsOutputItem()
        {
            yield return SpawnServices();
            AddInventoryItem(_hydrogenSO.itemId, 2);
            var recipe = MakeRecipe("hydrogen", 2, "proton", 1);
            ManualCraftService.Instance.TryCraft(recipe);

            var buf = _em.GetBuffer<InventorySlot>(_playerEntity, isReadOnly: true);
            int produced = SlotBufferUtils.CountInInventory(buf, _protonSO.itemId);
            Assert.AreEqual(1, produced, "TryCraft must deposit the output item into the inventory");
        }

        // ── GetInventoryCounts ────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator GetInventoryCounts_ReflectsCurrentBuffer()
        {
            yield return SpawnServices();
            AddInventoryItem(_hydrogenSO.itemId, 5);
            AddInventoryItem(_protonSO.itemId, 3);

            var counts = ManualCraftService.Instance.GetInventoryCounts();
            Assert.AreEqual(5, counts[_hydrogenSO.itemId],
                "GetInventoryCounts must report correct quantity for hydrogen");
            Assert.AreEqual(3, counts[_protonSO.itemId],
                "GetInventoryCounts must report correct quantity for proton");
        }
    }
}
