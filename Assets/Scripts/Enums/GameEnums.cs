using System;

namespace MobileIdleBuilder
{
    public enum ItemCategory
    {
        RawResource,
        Nucleon,
        Element,
        Isotope,
        Molecule,
        Alloy,
        Component,
        Particle,
        Megastructure
    }

    public enum RecipeCategory
    {
        Quark,
        Lepton,
        Nucleon,
        Element,
        Isotope,
        Molecule,
        Alloy,
        Component,
        Fusion,
        Fission
    }

    public enum BuildingCategory
    {
        Core,
        Transient,
        Power,
        Megastructure
    }

    public enum FieldType
    {
        None,
        Quark,    // Generic quark field — collector buildings specify up/down quark output via their recipe
        Lepton    // Electron field — outputItem on FieldSO is Electron
    }

    public enum DecayType
    {
        None,
        Alpha,
        Beta
    }

    [Flags]
    public enum ParticleUse
    {
        None             = 0,
        SecondaryParticle = 1,
        EnergyRecovery   = 2,
        RecipeInput      = 4
    }

    public enum PlacementRule
    {
        Anywhere,
        MustBeOnField,
        AdjacentToBuilding
    }

    /// <summary>Cardinal output direction for field collector buildings.</summary>
    public enum OutputDirection
    {
        North = 0, // +Z
        East  = 1, // +X
        South = 2, // -Z
        West  = 3  // -X
    }

    public enum PortType
    {
        Input  = 0,
        Output = 1
    }

    public enum ResearchBranch
    {
        Chemistry,
        Nuclear,
        Materials,
        Engineering,
        Astrophysics
    }

    public enum BuildEnvironment
    {
        Dev,
        Staging,
        Prod
    }

    public enum UpgradeEffectType
    {
        CraftSpeedMultiplier,
        VaultCapacity,
        ResearchSpeed,
        DecayCollectionRate,
        BuildingStartPrePlaced,
        OutputQuantityMultiplier,
        BuildingCostReduction,
        PrestigeMemoryDiscount
    }

    public enum AchievementTrigger
    {
        CraftItem,
        PlaceBuilding,
        CompleteResearch,
        ReachTier,
        Prestige,
        WinPVP,
        UnlockCodex
    }

    public enum CosmeticType
    {
        BuildingSkin,
        ParticleEffect,
        GridTheme,
        HUDAccent,
        MusicTrack,
        CodexCover,
        ProfileBadge
    }

    public enum UnlockMethod
    {
        Achievement,
        PremiumCurrency,
        IAP,
        Event
    }

    public enum ScreenTarget
    {
        MainScreen,
        ResearchScreen,
        PrestigeScreen
    }

    public enum TutorialStep
    {
        None                  = 0,
        IntroDialogue         = 1,   // Show Architect intro; TutorialOverlayController advances this
        CollectFirstElectron  = 2,   // Wait for player to collect 5 electrons (lepton field only)
        DirectToMaxwellsDemon = 3,   // Highlight Maxwell's Demon; wait for player to open it
        SellElectronsInDemon  = 4,   // Demon panel open; highlight electrons, prompt drag-to-sell
        CloseDemonPanel       = 5,   // Electrons sold; prompt player to close the panel
        BuyRecombinationI     = 6,   // Wait for Recombination I research to be purchased
        CraftFirstQuarks      = 7,
        CraftFirstProton      = 8,
        CraftFirstNeutron     = 9,
        CraftFirstHydrogen    = 10,
        PlaceFirstBuilding    = 11,
        AutomationStarted     = 12,
        ReachPrestigeWall     = 13,
        PrestigePromptShown   = 14,
        FirstPrestigeComplete = 15,
        SpendPrestigeCurrency = 16,
        Completed             = 99
    }
}
