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
    }
}
