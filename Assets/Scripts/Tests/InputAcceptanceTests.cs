using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>Pure rules for a belt tail handing an item to a building input (#200).</summary>
    [TestFixture]
    public class InputAcceptanceTests
    {
        [Test]
        public void NoRecipe_UnderCap_Accepts()
            => Assert.AreEqual(InputDecision.Accept, InputAcceptance.Decide(5, 30, false, false, false));

        [Test]
        public void NoRecipe_AtCap_Waits_NeverOverflows()
            => Assert.AreEqual(InputDecision.Wait, InputAcceptance.Decide(30, 30, false, false, true));

        [Test]
        public void Recipe_UsedItem_UnderCap_Accepts()
            => Assert.AreEqual(InputDecision.Accept, InputAcceptance.Decide(29, 30, true, true, false));

        [Test]
        public void Recipe_UsedItem_AtCap_NothingNeededBehind_Waits()
            => Assert.AreEqual(InputDecision.Wait, InputAcceptance.Decide(30, 30, true, true, false));

        [Test]
        public void Recipe_UsedItem_AtCap_NeededItemBehind_Overflows()
            => Assert.AreEqual(InputDecision.Overflow, InputAcceptance.Decide(30, 30, true, true, true));

        [Test]
        public void Recipe_UnusedItem_IsRefused_EvenUnderCap()
            => Assert.AreEqual(InputDecision.Wait, InputAcceptance.Decide(0, 30, true, false, false));

        [Test]
        public void Recipe_UnusedItem_JammingNeededItem_Overflows()
            => Assert.AreEqual(InputDecision.Overflow, InputAcceptance.Decide(0, 30, true, false, true));

        // ── Tail input priority (#275) ────────────────────────────────────────

        [Test]
        public void TailPriority_BuildingBesideTail_BeatsDemonBesideTail()
            => Assert.Less(TailInputPriority.Rank(false, false), TailInputPriority.Rank(false, true),
                "A proton belt ending beside both the Demon and the Atom Generator must feed the Atom Generator");

        [Test]
        public void TailPriority_BuildingBeside_BeatsDemonTheBeltPointsAt()
            => Assert.Less(TailInputPriority.Rank(false, false), TailInputPriority.Rank(true, true),
                "A cut belt re-aims at the Demon, so pointing at it must not beat a building that needs the item");

        [Test]
        public void TailPriority_WithinAKind_PointedInputFirst()
        {
            Assert.Less(TailInputPriority.Rank(true, false), TailInputPriority.Rank(false, false));
            Assert.Less(TailInputPriority.Rank(true, true),  TailInputPriority.Rank(false, true));
        }
    }
}
