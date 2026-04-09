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
        None               = 0,
        CraftFirstQuarks   = 1,
        CraftFirstProton   = 2,
        CraftFirstNeutron  = 3,
        CraftFirstHydrogen = 4,
        PlaceFirstBuilding = 5,
        AutomationStarted  = 6,
        ReachPrestigeWall  = 7,
        PrestigePromptShown = 8,
        FirstPrestigeComplete = 9,
        SpendPrestigeCurrency = 10,
        Completed          = 99
    }
}
