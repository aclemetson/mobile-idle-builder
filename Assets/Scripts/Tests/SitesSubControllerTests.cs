using NUnit.Framework;
using MobileIdleBuilder;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Edit-mode tests for the pure row-classification helper of SitesSubController.
    /// The button wiring / panel open-close path needs Play Mode + a live UIDocument
    /// and is covered by the in-editor manual check.
    /// </summary>
    [TestFixture]
    public class SitesSubControllerTests
    {
        [Test]
        public void ClassifyRow_ActiveSite_IsActive_RegardlessOfAffordability()
        {
            Assert.AreEqual(SitesSubController.SiteRowAction.Active,
                SitesSubController.ClassifyRow(isActive: true, isUnlocked: true, canAfford: false));
            Assert.AreEqual(SitesSubController.SiteRowAction.Active,
                SitesSubController.ClassifyRow(isActive: true, isUnlocked: true, canAfford: true));
        }

        [Test]
        public void ClassifyRow_UnlockedNonActive_IsTravel()
        {
            Assert.AreEqual(SitesSubController.SiteRowAction.Travel,
                SitesSubController.ClassifyRow(isActive: false, isUnlocked: true, canAfford: false));
        }

        [Test]
        public void ClassifyRow_LockedAffordable_IsUnlock()
        {
            Assert.AreEqual(SitesSubController.SiteRowAction.Unlock,
                SitesSubController.ClassifyRow(isActive: false, isUnlocked: false, canAfford: true));
        }

        [Test]
        public void ClassifyRow_LockedUnaffordable_IsLocked()
        {
            Assert.AreEqual(SitesSubController.SiteRowAction.Locked,
                SitesSubController.ClassifyRow(isActive: false, isUnlocked: false, canAfford: false));
        }

        // ── One-shot domains intro gate ──────────────────────────────────────

        [Test]
        public void ShouldShowDomainsIntro_GateResearch_FirstTime_IsTrue()
        {
            Assert.IsTrue(SitesSubController.ShouldShowDomainsIntro(
                SitesSubController.DomainsGateResearchId, alreadySeen: false));
        }

        [Test]
        public void ShouldShowDomainsIntro_GateResearch_AlreadySeen_IsFalse()
        {
            Assert.IsFalse(SitesSubController.ShouldShowDomainsIntro(
                SitesSubController.DomainsGateResearchId, alreadySeen: true));
        }

        [Test]
        public void ShouldShowDomainsIntro_OtherResearch_IsFalse()
        {
            Assert.IsFalse(SitesSubController.ShouldShowDomainsIntro("automation_i", alreadySeen: false));
        }
    }
}
