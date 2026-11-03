using NUnit.Framework;
using MobileIdleBuilder;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Edit-mode tests for the pure helpers of WorldsSubController: row classification and the
    /// one-shot world-intro gate. The button wiring / panel open-close path needs Play Mode + a live
    /// UIDocument and is covered by the in-editor manual check.
    /// </summary>
    [TestFixture]
    public class WorldsSubControllerTests
    {
        [Test]
        public void ClassifyRow_Active_IsActive_RegardlessOfState()
        {
            Assert.AreEqual(WorldsSubController.WorldRowAction.Active,
                WorldsSubController.ClassifyRow(isActive: true, isUnlocked: true, prereqsMet: false, canAfford: false));
        }

        [Test]
        public void ClassifyRow_UnlockedNonActive_IsTravel()
        {
            Assert.AreEqual(WorldsSubController.WorldRowAction.Travel,
                WorldsSubController.ClassifyRow(isActive: false, isUnlocked: true, prereqsMet: false, canAfford: false));
        }

        [Test]
        public void ClassifyRow_PrereqsMetAndAffordable_IsUnlock()
        {
            Assert.AreEqual(WorldsSubController.WorldRowAction.Unlock,
                WorldsSubController.ClassifyRow(isActive: false, isUnlocked: false, prereqsMet: true, canAfford: true));
        }

        [Test]
        public void ClassifyRow_PrereqsNotMet_IsLocked_EvenIfAffordable()
        {
            Assert.AreEqual(WorldsSubController.WorldRowAction.Locked,
                WorldsSubController.ClassifyRow(isActive: false, isUnlocked: false, prereqsMet: false, canAfford: true));
        }

        [Test]
        public void ClassifyRow_Unaffordable_IsLocked_EvenIfPrereqsMet()
        {
            Assert.AreEqual(WorldsSubController.WorldRowAction.Locked,
                WorldsSubController.ClassifyRow(isActive: false, isUnlocked: false, prereqsMet: true, canAfford: false));
        }

        // ── One-shot world intro ─────────────────────────────────────────────

        [Test]
        public void IntroDialogueId_FollowsConvention()
        {
            Assert.AreEqual("intro_world_chemistry", WorldsSubController.IntroDialogueId("world_chemistry"));
            Assert.AreEqual("intro_world_biology",   WorldsSubController.IntroDialogueId("world_biology"));
            Assert.IsNull(WorldsSubController.IntroDialogueId(null));
        }

        [Test]
        public void ShouldShowWorldIntro_FirstTime_IsTrue()
        {
            Assert.IsTrue(WorldsSubController.ShouldShowWorldIntro("world_chemistry", alreadySeen: false));
        }

        [Test]
        public void ShouldShowWorldIntro_AlreadySeen_IsFalse()
        {
            Assert.IsFalse(WorldsSubController.ShouldShowWorldIntro("world_chemistry", alreadySeen: true));
        }
    }
}
