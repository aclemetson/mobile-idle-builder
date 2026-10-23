using System.Collections.Generic;
using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>The have/need line under gather-step hints (#260).</summary>
    [TestFixture]
    public class TutorialHintProgressTests
    {
        static readonly Dictionary<int, string> Names = new Dictionary<int, string>
        {
            { 1, "Up Quark" }, { 2, "Down Quark" }, { 3, "Electron" },
        };

        static TutorialConditionDef InventoryMin(params (int id, int qty)[] items)
        {
            var c = new TutorialConditionDef { type = ConditionType.InventoryMin, items = new List<ItemCountReq>() };
            foreach (var (id, qty) in items) c.items.Add(new ItemCountReq { itemId = id, quantity = qty });
            return c;
        }

        static string Format(TutorialConditionDef c, Dictionary<int, int> inv) =>
            TutorialHintProgress.Format(c,
                id => inv.TryGetValue(id, out var n) ? n : 0,
                id => Names.TryGetValue(id, out var s) ? s : null);

        [Test]
        public void GatherForHydrogen_ShowsEachItemHaveOverNeed()
        {
            var c = InventoryMin((1, 4), (2, 2), (3, 2));
            var inv = new Dictionary<int, int> { { 1, 3 }, { 2, 2 } };
            Assert.AreEqual("Up Quark 3/4, Down Quark 2/2, Electron 0/2", Format(c, inv));
        }

        [Test]
        public void Overflow_IsCappedAtGoal()
        {
            var c = InventoryMin((1, 4));
            Assert.AreEqual("Up Quark 4/4", Format(c, new Dictionary<int, int> { { 1, 9 } }));
        }

        [Test]
        public void UnknownItemName_FallsBackToId()
        {
            var c = InventoryMin((42, 1));
            Assert.AreEqual("Item 42 0/1", Format(c, new Dictionary<int, int>()));
        }

        [Test]
        public void NonInventoryCondition_ReturnsNull()
        {
            var c = new TutorialConditionDef { type = ConditionType.ResearchUnlocked, researchId = "x" };
            Assert.IsNull(Format(c, new Dictionary<int, int>()));
            Assert.IsNull(Format(null, new Dictionary<int, int>()));
        }

        [Test]
        public void EmptyItemList_ReturnsNull()
        {
            Assert.IsNull(Format(InventoryMin(), new Dictionary<int, int>()));
        }
    }
}
