using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    [TestFixture]
    public class RollingRateTests
    {
        [Test]
        public void SteadyIncome_OverFullWindow_GivesRate()
        {
            var r = new RollingRate(90);
            for (int i = 0; i < 70; i++) r.Add(i, i * 10.0);
            Assert.AreEqual(10.0, r.Rate(60), 0.0001);
        }

        [Test]
        public void PartialWindow_UsesSamplesSoFar()
        {
            var r = new RollingRate(90);
            for (int i = 0; i < 5; i++) r.Add(i, i * 4.0);
            Assert.AreEqual(4.0, r.Rate(60), 0.0001);
        }

        [Test]
        public void IncomeStops_RateFallsTowardZero()
        {
            var r = new RollingRate(90);
            for (int i = 0; i <= 30; i++) r.Add(i, i * 10.0);
            for (int i = 31; i <= 90; i++) r.Add(i, 300.0);
            Assert.AreEqual(0.0, r.Rate(60), 0.0001, "no income in the last 60 s");
            Assert.Greater(r.Rate(60), -0.0001);
        }

        [Test]
        public void DropInTotal_ClearsBuffer()
        {
            var r = new RollingRate(90);
            for (int i = 0; i < 10; i++) r.Add(i, 100.0 + i * 10.0);
            r.Add(10, 5.0);
            Assert.AreEqual(1, r.Count);
            Assert.AreEqual(0.0, r.Rate(60), 0.0001);
            r.Add(11, 15.0);
            Assert.AreEqual(10.0, r.Rate(60), 0.0001);
        }

        [Test]
        public void FewerThanTwoSamples_GivesZero()
        {
            var r = new RollingRate(90);
            Assert.AreEqual(0.0, r.Rate(60));
            r.Add(0, 50.0);
            Assert.AreEqual(0.0, r.Rate(60));
        }

        [Test]
        public void Clear_EmptiesBuffer()
        {
            var r = new RollingRate(90);
            r.Add(0, 0); r.Add(1, 10);
            r.Clear();
            Assert.AreEqual(0, r.Count);
        }
    }
}
