using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// When a belt placed on a ConveyorLinked tutorial step links nothing, the player is told why instead of
    /// the step sitting silently (#255). Steps that need several links must not warn after a good first belt.
    /// </summary>
    [TestFixture]
    public class TutorialBeltFeedbackTests
    {
        [Test]
        public void StepAdvanced_NoWarning() =>
            Assert.IsFalse(TutorialOverlayController.IsBeltUnlinked(stepAdvanced: true, linksBefore: 0, linksAfter: 0));

        [Test]
        public void BeltLinkedNothing_Warns() =>
            Assert.IsTrue(TutorialOverlayController.IsBeltUnlinked(stepAdvanced: false, linksBefore: 0, linksAfter: 0));

        [Test]
        public void FirstOfTwoLinks_NoWarning() =>
            Assert.IsFalse(TutorialOverlayController.IsBeltUnlinked(stepAdvanced: false, linksBefore: 0, linksAfter: 1));

        [Test]
        public void SecondBeltLinkedNothing_Warns() =>
            Assert.IsTrue(TutorialOverlayController.IsBeltUnlinked(stepAdvanced: false, linksBefore: 1, linksAfter: 1));

        [Test]
        public void BaselineNotYetEvaluated_CountsAsZero() =>
            Assert.IsTrue(TutorialOverlayController.IsBeltUnlinked(stepAdvanced: false, linksBefore: -1, linksAfter: 0));

        [Test]
        public void StepNoLongerEvaluated_NoWarning() =>
            Assert.IsFalse(TutorialOverlayController.IsBeltUnlinked(stepAdvanced: false, linksBefore: 0, linksAfter: -1));
    }
}
