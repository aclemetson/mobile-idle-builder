using NUnit.Framework;
using L = MobileIdleBuilder.BackNavigation.Layer;

namespace MobileIdleBuilder.Tests
{
    /// <summary>Android back / Escape layer order (#205): one press, one layer, top-most first.</summary>
    [TestFixture]
    public class BackNavigationTests
    {
        static L Top(BackNavigation.State s) => BackNavigation.TopLayer(s);

        [Test]
        public void NothingOpen_DoesNothing() =>
            Assert.AreEqual(L.None, Top(new BackNavigation.State()));

        [Test]
        public void Drawer_Alone_ClosesDrawer() =>
            Assert.AreEqual(L.Drawer, Top(new BackNavigation.State { DrawerOpen = true }));

        [Test]
        public void PanelOverDrawer_ClosesPanelFirst() =>
            Assert.AreEqual(L.Panel, Top(new BackNavigation.State { PanelOpen = true, DrawerOpen = true }));

        [Test]
        public void InspectorOverPanel_ClosesInspectorFirst() =>
            Assert.AreEqual(L.Inspector, Top(new BackNavigation.State { InspectorOpen = true, PanelOpen = true }));

        [Test]
        public void Modals_BeatEverythingBelowThem()
        {
            var below = new BackNavigation.State { InspectorOpen = true, PanelOpen = true, DrawerOpen = true };

            var s = below; s.UpdateNoticeOpen = true;
            Assert.AreEqual(L.UpdateNotice, Top(s));
            s.IdleReturnOpen = true;
            Assert.AreEqual(L.IdleReturn, Top(s));
            s.SurgeOfferOpen = true;
            Assert.AreEqual(L.SurgeOffer, Top(s));
            s.ConfirmOpen = true;
            Assert.AreEqual(L.ConfirmPrompt, Top(s));
        }

        [Test]
        public void Dialogue_IsNeverSkipped_PressIsSwallowed()
        {
            var s = new BackNavigation.State
            {
                DialoguePlaying = true, ConfirmOpen = true, PanelOpen = true, DrawerOpen = true,
            };
            Assert.AreEqual(L.SwallowDialogue, Top(s), "Back must not close anything under a playing dialogue");
        }

        [Test]
        public void ActiveOrJustEndedMode_YieldsToTheModeController()
        {
            // The placement / conveyor / deconstruct controllers cancel themselves on Escape. The HUD
            // must not also close a panel or the drawer with the same press.
            var s = new BackNavigation.State
            {
                ModeActiveOrJustEnded = true, PanelOpen = true, DrawerOpen = true, DialoguePlaying = true,
            };
            Assert.AreEqual(L.YieldToMode, Top(s));
        }
    }
}
