namespace MobileIdleBuilder
{
    /// <summary>
    /// Android back button / Escape (#205): which single UI layer one press should act on.
    /// Pure so the priority order is unit-tested; HUDController reads the live state and applies it.
    ///
    /// One press closes exactly one layer, top-most first. Tutorial dialogue is never skipped by back
    /// (the press is swallowed). Placement, conveyor and deconstruct modes already cancel themselves on
    /// Escape in their own controllers, so while one is active (or ended this frame) the HUD yields
    /// rather than closing a second layer with the same press. With nothing open, back does nothing.
    /// </summary>
    public static class BackNavigation
    {
        public enum Layer
        {
            None,
            /// <summary>A mode controller owns this press (it cancels the mode itself).</summary>
            YieldToMode,
            /// <summary>Dialogue is playing: swallow the press, never skip it.</summary>
            SwallowDialogue,
            /// <summary>The ad test mode card: back skips it (no reward), like dismissing an ad early.</summary>
            TestAd,
            ConfirmPrompt,
            /// <summary>The Entropy Surge offer card: back closes it like its X (the offer stays).</summary>
            SurgeOffer,
            IdleReturn,
            UpdateNotice,
            Inspector,
            Panel,
            Drawer,
        }

        public struct State
        {
            public bool ModeActiveOrJustEnded;
            public bool DialoguePlaying;
            public bool TestAdOpen;
            public bool ConfirmOpen;
            public bool SurgeOfferOpen;
            public bool IdleReturnOpen;
            public bool UpdateNoticeOpen;
            public bool InspectorOpen;
            public bool PanelOpen;
            public bool DrawerOpen;
        }

        public static Layer TopLayer(in State s)
        {
            if (s.ModeActiveOrJustEnded) return Layer.YieldToMode;
            if (s.DialoguePlaying)       return Layer.SwallowDialogue;
            if (s.TestAdOpen)            return Layer.TestAd;
            if (s.ConfirmOpen)           return Layer.ConfirmPrompt;
            if (s.SurgeOfferOpen)        return Layer.SurgeOffer;
            if (s.IdleReturnOpen)        return Layer.IdleReturn;
            if (s.UpdateNoticeOpen)      return Layer.UpdateNotice;
            if (s.InspectorOpen)         return Layer.Inspector;
            if (s.PanelOpen)             return Layer.Panel;
            if (s.DrawerOpen)            return Layer.Drawer;
            return Layer.None;
        }
    }
}
