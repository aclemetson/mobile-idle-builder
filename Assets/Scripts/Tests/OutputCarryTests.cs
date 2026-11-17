using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    [TestFixture]
    public class OutputCarryTests
    {
        [Test]
        public void WholeMultiplier_PaysThatManyEveryTime()
        {
            float carry = 0f;
            for (int i = 0; i < 5; i++) Assert.AreEqual(3, OutputCarry.Take(3f, ref carry));
            Assert.AreEqual(0f, carry, 1e-6f);
        }

        [Test]
        public void FractionalMultiplier_AveragesExactly()
        {
            // 1.1x: nine crafts of 1 then one of 2 -> 11 over 10 crafts.
            float carry = 0f;
            int total = 0;
            for (int i = 0; i < 10; i++) total += OutputCarry.Take(1.1f, ref carry);
            Assert.AreEqual(11, total);
        }

        [Test]
        public void ZeroOrNegativeMultiplier_PaysNothing()
        {
            float carry = 0.5f;
            Assert.AreEqual(0, OutputCarry.Take(0f, ref carry));
            Assert.AreEqual(0, OutputCarry.Take(-1f, ref carry));
            Assert.AreEqual(0.5f, carry, 1e-6f);
        }
    }
}
