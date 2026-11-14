using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>Backoff used when the ad SDK fails to init or a rewarded ad fails to load.</summary>
    public class AdLoadRetryTests
    {
        [Test]
        public void DelayFor_DoublesFromFirstDelay()
        {
            Assert.AreEqual(5f,  AdLoadRetry.DelayFor(1));
            Assert.AreEqual(10f, AdLoadRetry.DelayFor(2));
            Assert.AreEqual(20f, AdLoadRetry.DelayFor(3));
            Assert.AreEqual(80f, AdLoadRetry.DelayFor(5));
        }

        [Test]
        public void DelayFor_CapsAtMax()
        {
            Assert.AreEqual(AdLoadRetry.MaxDelaySeconds, AdLoadRetry.DelayFor(6));
            Assert.AreEqual(AdLoadRetry.MaxDelaySeconds, AdLoadRetry.DelayFor(1000));
        }

        [Test]
        public void DelayFor_NoFailures_IsZero()
            => Assert.AreEqual(0f, AdLoadRetry.DelayFor(0));

        [Test]
        public void OnFailed_KeepsBackingOff_UntilASuccessResetsIt()
        {
            var retry = new AdLoadRetry();
            Assert.AreEqual(5f,  retry.OnFailed());
            Assert.AreEqual(10f, retry.OnFailed());
            Assert.AreEqual(2, retry.Failures);

            retry.OnSucceeded();
            Assert.AreEqual(0, retry.Failures);
            Assert.AreEqual(5f, retry.OnFailed());
        }

        [Test]
        public void OnFailed_NeverGivesUp()
        {
            var retry = new AdLoadRetry();
            float last = 0f;
            for (int i = 0; i < 50; i++) last = retry.OnFailed();
            Assert.AreEqual(AdLoadRetry.MaxDelaySeconds, last);
        }
    }
}
