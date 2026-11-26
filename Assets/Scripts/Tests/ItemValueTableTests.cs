using System.Collections.Generic;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// #284: NetWorthSystem and EntropySinkSystem price items from ItemValueTable (a blob built by
    /// ItemValueTableSystem) so they can be Burst-compiled. The table must follow remote value overrides,
    /// price unknown items like the Demon always did, and keep net worth exact above 2^24.
    /// </summary>
    [TestFixture]
    public class ItemValueTableTests
    {
        readonly List<ItemSO> _created = new();
        World _world;
        EntityManager _em;
        Entity _player, _sink;

        ItemSO Item(string id, int itemId, float value)
        {
            var so = ScriptableObject.CreateInstance<ItemSO>();
            so.id = id; so.itemId = itemId; so.baseSellValue = value;
            _created.Add(so);
            return so;
        }

        [SetUp]
        public void SetUp()
        {
            ItemStars.Invalidate();
            _world = new World("ItemValueTable");
            _em    = _world.EntityManager;
            var group = _world.CreateSystemManaged<SimulationSystemGroup>();
            group.AddSystemToUpdateList(_world.CreateSystemManaged<ItemValueTableSystem>());
            group.AddSystemToUpdateList(_world.CreateSystem<EntropySinkSystem>());
            group.AddSystemToUpdateList(_world.CreateSystem<NetWorthSystem>());
            group.SortSystems();

            _player = _em.CreateEntity();
            _em.AddComponent<PlayerInventoryTag>(_player);
            _em.AddComponentData(_player, new PlayerProgressData());
            _em.AddBuffer<InventorySlot>(_player);

            _sink = _em.CreateEntity();
            _em.AddComponent<EntropySinkTag>(_sink);
            _em.AddComponentData(_sink, new BuildingData { IsActive = true });
            _em.AddBuffer<BuildingInputSlot>(_sink);
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
            ItemDatabase.InjectForTesting(new ItemSO[0]);
            foreach (var so in _created) if (so != null) Object.DestroyImmediate(so);
            _created.Clear();
            ItemStars.Invalidate();
        }

        long Currency => _em.GetComponentData<PlayerProgressData>(_player).BaseCurrency;

        void Sell(int itemId, int quantity)
        {
            SlotBufferUtils.AddToInputBuffer(_em.GetBuffer<BuildingInputSlot>(_sink), itemId, quantity);
            _world.Update();
        }

        [Test]
        public void TheSinkPaysEachItemsValue_AndOneForUnknownOrWorthlessItems()
        {
            ItemDatabase.InjectForTesting(new[] { Item("carbon", 6, 10f), Item("dust", 2, 0f) });
            Sell(6, 3);
            Assert.AreEqual(30L, Currency);
            Sell(2, 4);
            Assert.AreEqual(34L, Currency, "a worthless item pays 1");
            Sell(999, 5);
            Assert.AreEqual(39L, Currency, "an id outside the table pays 1");
        }

        [Test]
        public void ARemoteValueOverride_RepricesTheNextFrame()
        {
            var carbon = Item("carbon", 6, 10f);
            var all = new[] { carbon };
            ItemDatabase.InjectForTesting(all);
            Sell(6, 1);
            Assert.AreEqual(10L, Currency);

            int applied = GameDataOverrides.ApplyItems(all, new[]
            {
                new GameDataOverrides.EntityEntry { id = "carbon", field = "baseSellValue", value = 25f },
            });
            Assert.AreEqual(1, applied);

            Sell(6, 1);
            Assert.AreEqual(35L, Currency, "10 before the override, 25 after");
        }

        [Test]
        public void NetWorth_CountsInventoryFromTheTable_AndStaysExactAbove2To24()
        {
            ItemDatabase.InjectForTesting(new[] { Item("carbon", 6, 10f) });
            _em.GetBuffer<InventorySlot>(_player).Add(new InventorySlot { ItemID = 6, Quantity = 4 });
            var p = _em.GetComponentData<PlayerProgressData>(_player);
            p.BaseCurrency = 50_000_001;
            _em.SetComponentData(_player, p);

            _world.Update();

            Assert.AreEqual(50_000_041d, _em.GetComponentData<PlayerProgressData>(_player).NetWorth);
        }

        [Test]
        public void StoredItems_CountAtTheDemonPrice()
        {
            ItemDatabase.InjectForTesting(new[] { Item("carbon", 6, 10f) });
            var crafter = _em.CreateEntity();
            _em.AddComponentData(crafter, new BuildingData { IsActive = true });
            _em.AddBuffer<BuildingOutputSlot>(crafter).Add(new BuildingOutputSlot { ItemID = 6, Quantity = 2 });
            _em.AddBuffer<BuildingInputSlot>(crafter).Add(new BuildingInputSlot { ItemID = 777, Quantity = 3 });

            _world.Update();

            Assert.AreEqual(23d, _em.GetComponentData<PlayerProgressData>(_player).StoredItemValue,
                "2 carbon x 10 + 3 unknown x 1");
        }
    }
}
