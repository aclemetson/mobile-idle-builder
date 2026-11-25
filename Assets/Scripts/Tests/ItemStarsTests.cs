using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// PvE phase 2: tournament item stars. +20% value per star, additive; permanent; drawn from discovered
    /// items; applied to every runtime price (Demon/sink payout, surplus price, inventory net worth).
    /// </summary>
    public class ItemStarsTests
    {
        sealed class KnowledgeStub : IRecipeKnowledgeService
        {
            readonly HashSet<string> _known;
            public KnowledgeStub(params string[] known) => _known = new HashSet<string>(known);
            public bool IsKnown(string recipeId) => _known.Contains(recipeId);
            public void MarkKnown(string recipeId) => _known.Add(recipeId);
        }

        readonly List<ItemSO> _created = new();
        GameObject _saveManagerGO;
        string _savePath, _saveBackup;

        [SetUp]
        public void SetUp()
        {
            ItemStars.Invalidate();
            _savePath   = Path.Combine(LocalSaveService.SaveDirectory, "save.json");
            _saveBackup = File.Exists(_savePath) ? File.ReadAllText(_savePath) : null;
        }

        [TearDown]
        public void TearDown()
        {
            ItemDatabase.InjectForTesting(new ItemSO[0]);
            foreach (var so in _created) if (so != null) UnityEngine.Object.DestroyImmediate(so);
            _created.Clear();
            if (_saveManagerGO != null)
            {
                UnityEngine.Object.DestroyImmediate(_saveManagerGO);
                _saveManagerGO = null;
                new LocalSaveService().Delete();
                if (_saveBackup != null) File.WriteAllText(_savePath, _saveBackup);
            }
            ItemStars.Invalidate();
        }

        ItemSO MakeItem(string id, int itemId, float value)
        {
            var item = ScriptableObject.CreateInstance<ItemSO>();
            item.id = id; item.itemId = itemId; item.baseSellValue = value;
            _created.Add(item);
            return item;
        }

        static RecipeJson Recipe(string id, string output, params string[] inputs)
        {
            var r = new RecipeJson { id = id, output = new RecipeOutputJson { id = output, quantity = 1 },
                                     inputs = new List<RecipeIngredientJson>() };
            foreach (var i in inputs) r.inputs.Add(new RecipeIngredientJson { id = i, quantity = 1 });
            return r;
        }

        SaveData SpawnSaveManager()
        {
            new LocalSaveService().Delete();
            _saveManagerGO = new GameObject("SaveManager");
            var sm = _saveManagerGO.AddComponent<SaveManager>();
            var t = sm.GetType();
            while (t != null && t != typeof(MonoBehaviour))
            {
                var m = t.GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
                                             | BindingFlags.DeclaredOnly);
                if (m != null) { m.Invoke(sm, null); break; }
                t = t.BaseType;
            }
            Assert.IsNotNull(SaveManager.Instance?.Current);
            return SaveManager.Instance.Current;
        }

        // ── Rules ─────────────────────────────────────────────────────────────

        [TestCase(0, 1f)] [TestCase(1, 1.2f)] [TestCase(5, 2f)] [TestCase(10, 3f)] [TestCase(-3, 1f)]
        public void Multiplier_IsAdditiveTwentyPercentPerStar(int stars, float expected) =>
            Assert.AreEqual(expected, ItemStars.MultiplierFor(stars), 1e-5f);

        [Test]
        public void Add_CountsPerItem_AndTotals()
        {
            var save = new SaveData();
            ItemStars.Add(save, "carbon", 2);
            ItemStars.Add(save, "oxygen", 1);
            ItemStars.Add(save, "carbon", 3);
            Assert.AreEqual(5, ItemStars.Count(save, "carbon"));
            Assert.AreEqual(1, ItemStars.Count(save, "oxygen"));
            Assert.AreEqual(0, ItemStars.Count(save, "helium"));
            Assert.AreEqual(6, ItemStars.Total(save));
            Assert.AreEqual(save.itemStarIds.Count, save.itemStarCounts.Count);
        }

        [Test]
        public void Add_RepairsMismatchedLists_SoTheyStayParallel()
        {
            var save = new SaveData { itemStarIds = new List<string> { "a", "b" }, itemStarCounts = new List<int> { 1 } };
            ItemStars.Add(save, "c", 2);
            Assert.AreEqual(save.itemStarIds.Count, save.itemStarCounts.Count);
            Assert.AreEqual(2, ItemStars.Count(save, "c"));
        }

        [Test]
        public void Grant_DrawsOnlyFromThePool_OneStarPerDraw()
        {
            var save = new SaveData();
            var pool = new[] { "carbon", "oxygen", "neon" };
            var landed = ItemStars.Grant(save, 50, pool, new System.Random(7));
            Assert.AreEqual(50, landed.Count);
            Assert.AreEqual(50, ItemStars.Total(save));
            foreach (var id in save.itemStarIds) CollectionAssert.Contains(pool, id);
            Assert.Greater(save.itemStarIds.Count, 1, "50 draws over 3 items should not all land on one");
        }

        [Test]
        public void Grant_EmptyPool_GrantsNothing()
        {
            var save = new SaveData();
            Assert.AreEqual(0, ItemStars.Grant(save, 5, new string[0], new System.Random(1)).Count);
            Assert.AreEqual(0, ItemStars.Total(save));
        }

        [Test]
        public void DiscoveredItems_AreInputsAndOutputsOfKnownRecipes_Distinct()
        {
            ItemDatabase.InjectForTesting(new[]
            {
                MakeItem("up_quark", 1, 1), MakeItem("down_quark", 2, 1), MakeItem("proton", 3, 5),
                MakeItem("neutron", 4, 5), MakeItem("deuterium", 5, 20),
            });
            var recipes = new List<RecipeJson>
            {
                Recipe("proton", "proton", "up_quark", "up_quark", "down_quark"),
                Recipe("neutron", "neutron", "up_quark", "down_quark", "down_quark"),
                Recipe("deuterium", "deuterium", "proton", "neutron"),
                Recipe("ghost", "not_an_item", "proton"),
            };
            var pool = ItemStars.DiscoveredItemIds(recipes, new KnowledgeStub("proton", "neutron", "ghost"));
            CollectionAssert.AreEquivalent(new[] { "up_quark", "down_quark", "proton", "neutron" }, pool,
                "deuterium's recipe is unknown; not_an_item is not in the database");
        }

        [Test]
        public void Prestige_KeepsStars()
        {
            var save = new SaveData();
            ItemStars.Add(save, "carbon", 4);
            PrestigeRunReset.ResetRun(save, 250);
            Assert.AreEqual(4, ItemStars.Count(save, "carbon"));
        }

        // ── Live prices ───────────────────────────────────────────────────────

        [Test]
        public void LivePrices_UseStars_AndFollowNewGrants()
        {
            ItemDatabase.InjectForTesting(new[] { MakeItem("carbon", 6, 10f), MakeItem("air", 9, 0f) });
            var save = SpawnSaveManager();

            Assert.AreEqual(1f, ItemStars.Multiplier(6));
            Assert.AreEqual(10f, SurplusSale.DemonPrice(6), 1e-4f);

            ItemStars.Add(save, "carbon", 5);
            Assert.AreEqual(5, ItemStars.Count(6));
            Assert.AreEqual(20f, SurplusSale.DemonPrice(6), 1e-4f, "5 stars double the price");
            Assert.AreEqual(20f, ItemStars.StarredBase(ItemDatabase.GetStatic(6)), 1e-4f);

            // An item with no value still sells for 1, raised by its stars.
            ItemStars.Add(save, "air", 10);
            Assert.AreEqual(3f, SurplusSale.DemonPrice(9), 1e-4f);
            Assert.AreEqual(0f, ItemStars.StarredBase(ItemDatabase.GetStatic(9)), 1e-4f,
                "inventory net worth keeps a no-value item at 0");
        }

        [Test]
        public void LivePrices_FollowASwappedSave()
        {
            ItemDatabase.InjectForTesting(new[] { MakeItem("carbon", 6, 10f) });
            var save = SpawnSaveManager();
            ItemStars.Add(save, "carbon", 5);
            Assert.AreEqual(2f, ItemStars.Multiplier(6), 1e-5f);

            // A cloud save replaces Current with another object: its own stars apply.
            var other = new SaveData();
            ItemStars.Add(other, "carbon", 1);
            var field = typeof(SaveManager).GetField("_current", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "SaveManager._current renamed: update this test");
            field.SetValue(SaveManager.Instance, other);
            Assert.AreSame(other, SaveManager.Instance.Current);
            Assert.AreEqual(1.2f, ItemStars.Multiplier(6), 1e-5f);
        }

        [Test]
        public void LivePrices_NoSave_MeansNoStars()
        {
            ItemDatabase.InjectForTesting(new[] { MakeItem("carbon", 6, 10f) });
            Assert.IsTrue(SaveManager.Instance == null);
            Assert.AreEqual(1f, ItemStars.Multiplier(6));
        }

        // ── ECS: the Demon / sink payout and inventory net worth ──────────────

        [Test]
        public void Sink_PaysStarredValue()
        {
            ItemDatabase.InjectForTesting(new[] { MakeItem("carbon", 6, 10f) });
            var save = SpawnSaveManager();
            ItemStars.Add(save, "carbon", 5);

            using var world = new World("StarsSink");
            var em = world.EntityManager;
            var group = world.CreateSystemManaged<SimulationSystemGroup>();
            group.AddSystemToUpdateList(world.CreateSystemManaged<ItemValueTableSystem>());
            group.AddSystemToUpdateList(world.CreateSystem<EntropySinkSystem>());
            group.SortSystems();

            var player = em.CreateEntity();
            em.AddComponent<PlayerInventoryTag>(player);
            em.AddComponentData(player, new PlayerProgressData());
            var sink = em.CreateEntity();
            em.AddComponent<EntropySinkTag>(sink);
            em.AddComponentData(sink, new BuildingData { IsActive = true });
            var buf = em.AddBuffer<BuildingInputSlot>(sink);
            SlotBufferUtils.AddToInputBuffer(buf, 6, 3);

            world.Update();
            // The price table (#284) reads the injected item: 3 carbon x 10 x2 (5 stars).
            Assert.AreEqual(60L, em.GetComponentData<PlayerProgressData>(player).BaseCurrency);
        }

        [Test]
        public void Sink_RepricesWhenStarsAreGranted()
        {
            ItemDatabase.InjectForTesting(new[] { MakeItem("carbon", 6, 10f) });
            var save = SpawnSaveManager();

            using var world = new World("StarsReprice");
            var em = world.EntityManager;
            var group = world.CreateSystemManaged<SimulationSystemGroup>();
            group.AddSystemToUpdateList(world.CreateSystemManaged<ItemValueTableSystem>());
            group.AddSystemToUpdateList(world.CreateSystem<EntropySinkSystem>());
            group.SortSystems();

            var player = em.CreateEntity();
            em.AddComponent<PlayerInventoryTag>(player);
            em.AddComponentData(player, new PlayerProgressData());
            var sink = em.CreateEntity();
            em.AddComponent<EntropySinkTag>(sink);
            em.AddComponentData(sink, new BuildingData { IsActive = true });
            em.AddBuffer<BuildingInputSlot>(sink);

            SlotBufferUtils.AddToInputBuffer(em.GetBuffer<BuildingInputSlot>(sink), 6, 1);
            world.Update();
            Assert.AreEqual(10L, em.GetComponentData<PlayerProgressData>(player).BaseCurrency);

            ItemStars.Add(save, "carbon", 5);   // the price table (#284) must notice and rebuild
            SlotBufferUtils.AddToInputBuffer(em.GetBuffer<BuildingInputSlot>(sink), 6, 1);
            world.Update();
            Assert.AreEqual(30L, em.GetComponentData<PlayerProgressData>(player).BaseCurrency, "10, then 10 x2");
        }

        [Test]
        public void NetWorth_CountsInventoryAtStarredValue()
        {
            ItemDatabase.InjectForTesting(new[] { MakeItem("carbon", 6, 10f) });
            var save = SpawnSaveManager();
            ItemStars.Add(save, "carbon", 5);

            using var world = new World("StarsNetWorth");
            var em = world.EntityManager;
            var group = world.CreateSystemManaged<SimulationSystemGroup>();
            group.AddSystemToUpdateList(world.CreateSystemManaged<ItemValueTableSystem>());
            group.AddSystemToUpdateList(world.CreateSystem<NetWorthSystem>());
            group.SortSystems();

            var player = em.CreateEntity();
            em.AddComponent<PlayerInventoryTag>(player);
            em.AddComponentData(player, new PlayerProgressData());
            var inv = em.AddBuffer<InventorySlot>(player);
            inv.Add(new InventorySlot { ItemID = 6, Quantity = 4 });

            world.Update();
            Assert.AreEqual(80d, em.GetComponentData<PlayerProgressData>(player).NetWorth, 1e-6,
                "4 carbon x 10 x2 (5 stars)");
        }

        // ── UI text + save fields ─────────────────────────────────────────────

        [Test]
        public void WorthText_ShowsStarsOnlyWhenThereAreAny()
        {
            Assert.AreEqual("", HUDController.StarBadge(0));
            Assert.AreEqual("★3 +60%", HUDController.StarBadge(3));
            StringAssert.DoesNotContain("★", HUDController.WorthEachText(12f));
            StringAssert.EndsWith("· ★5 +100%", HUDController.WorthEachText(12f, 5));
            StringAssert.Contains("★2 +40%", HUDBuildingInspectorSubController.ValueLines(12f, 12d, 1f, 1d, false, 2)[0]);
        }

        [Test]
        public void Save_LegacyJsonWithoutStars_LoadsEmpty()
        {
            var save = JsonUtility.FromJson<SaveData>("{\"prestigeCount\":2}");
            Assert.IsNotNull(save.itemStarIds);
            Assert.IsNotNull(save.itemStarCounts);
            Assert.AreEqual(0, ItemStars.Total(save));
        }

        [Test]
        public void Save_Stars_RoundTrip()
        {
            var save = new SaveData();
            ItemStars.Add(save, "carbon", 3);
            ItemStars.Add(save, "oxygen", 1);
            var back = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(save));
            Assert.AreEqual(3, ItemStars.Count(back, "carbon"));
            Assert.AreEqual(1, ItemStars.Count(back, "oxygen"));
        }
    }
}
