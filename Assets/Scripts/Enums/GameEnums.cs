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
        Megastructure,
        OrganicCompound   // Chemistry C4 outputs (amino acids, sugars, lipids, nucleotides); Biology feedstock
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

    /// <summary>
    /// Which procedural structure BuildingVisualizer builds for a building, instead of the placeholder
    /// cube. Data-driven (set from BuildingSO.structureKind) so the visualizer switches on this rather
    /// than hardcoding building ids. Collector and EntropySink are still detected via their gameplay
    /// components (CollectorData / EntropySinkTag); the entries here are the bespoke producer forms.
    /// Keep in sync with the art direction in docs/agents/visual-design.md.
    /// </summary>
    public enum BuildingStructureKind
    {
        None = 0,           // placeholder cube
        AtomGenerator,      // orbital nucleus
        StrongForceCombiner,// confinement knot
        IsotopicManipulator,// breathing nucleus
        MolecularSynthesizer,// bonding lattice
        MaterialsForge,     // crucible
        ComponentFabricator,// precision crown
        RadioactiveContainment, // lead shell
        BasicGenerator,     // energy spire
        PowerRelay          // broadcast pylon
    }

    /// <summary>
    /// Field "type" is a free-form string id (data-driven — new field types need no code, only JSON).
    /// A <see cref="FieldSO"/>'s <c>fieldType</c> categorises it; <see cref="BuildingSO"/>'s
    /// <c>compatibleFields</c> lists the field-type ids a building can be placed on; and
    /// <c>TutorialFlowSO.collectionFilter</c> restricts manual collection to one type.
    /// The sentinel <see cref="None"/> ("None"/empty/null) means "no type / no restriction".
    /// Known ids in game_data.json today: None, Quark, Lepton, Uranium, Plutonium, Element.
    /// </summary>
    public static class FieldTypes
    {
        public const string None = "None";

        /// <summary>True when a field-type filter imposes no restriction (null, empty, or "None").</summary>
        public static bool IsUnrestricted(string fieldType) =>
            string.IsNullOrEmpty(fieldType) ||
            string.Equals(fieldType, None, StringComparison.OrdinalIgnoreCase);

        /// <summary>Normalises a raw field-type string: null/empty becomes the "None" sentinel.</summary>
        public static string Normalize(string fieldType) =>
            string.IsNullOrEmpty(fieldType) ? None : fieldType;
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
        FieldCooldownReduction,   // additive fraction per level off the field tap cooldown (+0.05 = 5 %)
        BuildingStartPrePlaced,
        OutputQuantityMultiplier,
        BuildingCostReduction,
        PrestigeMemoryDiscount,
        StartingEntropyBonus,     // flat entropy added to BaseCurrency at run start
        GlobalResearchDiscount,   // additive % off all research costs
        PrestigeGainMultiplier,   // % bonus to PC earned per prestige
        IdleTimeCap,              // additive seconds per level (+1800 = 30 min)
        IdleCollectionRate,       // additive fraction per level (+0.05 = 5 %)
    }

    public enum AchievementTrigger
    {
        CraftItem,
        PlaceBuilding,
        CompleteResearch,
        ReachTier,
        Prestige,
        WinPVP,
        UnlockCodex,
        Login,                        // fires once per game session start
        SpendEntropy,                 // delta = entropy amount spent (additive)
        TotalPrestigeCurrencyEarned   // delta = PC earned this prestige (additive across runs)
    }

    /// <summary>
    /// Determines reset cadence. Progression achievements are permanent milestones;
    /// Daily/Weekly/Monthly re-lock and reset at the start of each new period.
    /// </summary>
    public enum AchievementCategory
    {
        Progression,
        Daily,
        Weekly,
        Monthly
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

    /// <summary>
    /// Condition types used by TutorialConditionDef to determine when a step advances.
    /// Evaluated generically in TutorialSystem — no step names in code.
    /// </summary>
    public enum ConditionType
    {
        Auto,               // Advance immediately on entry
        InventoryMin,       // All (or any, if anyOf=true) listed items meet their minimum quantity
        InventoryZero,      // All listed item IDs have quantity == 0 in inventory
        UiEvent,            // A named UI event fires (demon_opened, demon_closed, dialogue_complete, …)
        ResearchUnlocked,   // A specific research ID appears in SaveManager.unlockedResearch
        BuildingMin,        // At least minCount BuildingData entities exist
        PrestigeRunMin,     // PrestigeData.RunCount >= minCount
        PrestigeAvailable   // PlayerProgressData.PrestigeAvailable == true
    }

    /// <summary>How TutorialOverlayController highlights a world target when a step activates.</summary>
    public enum HighlightMode
    {
        None,        // No highlight
        Full,        // Camera pan to target + amber tile pulse
        VisualOnly   // Amber tile pulse only — camera stays on player
    }

    /// <summary>Restricts which buildings the player may tap during a tutorial step.</summary>
    public enum BuildingInteractionGate
    {
        None,                // No restriction — all buildings are tappable (default)
        BlockAll,            // Block all building taps (e.g. while collecting resources)
        EntropySinkOnly,     // Only Maxwell's Demon may be opened (e.g. while directed to sell)
        AtomicAssemblerOnly  // Only the Atom Generator (atomic_assembler) may be opened
    }
}
