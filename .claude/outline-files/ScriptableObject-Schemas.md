# ScriptableObject Schemas
# All game data is defined in ScriptableObjects — never hardcoded.
# These schemas define every field for each SO type.
# The AI builder should generate one SO asset per entry in recipes.json.
# Field names here match recipes.json keys exactly for clean importing.

---

## ItemSO
**Path:** `Assets/ScriptableObjects/Items/`
**Naming:** `{id}.asset` e.g. `hydrogen.asset`

```csharp
[CreateAssetMenu(menuName = "Game/Item")]
public class ItemSO : ScriptableObject
{
    [Header("Identity")]
    public string id;                  // matches recipes.json id
    public string displayName;         // human-readable name
    public string symbol;              // chemical symbol or notation
    public Sprite icon;                // assigned in editor

    [Header("Classification")]
    public int tier;                   // 1 = subatomic, 2 = atomic, etc.
    public ItemCategory category;      // Enum: RawResource, Nucleon, Element, Isotope, Molecule, Alloy, Component, Particle, Megastructure
    public TierSO tierData;            // reference to TierSO

    [Header("Scientific Data")]
    public int atomicNumber;           // 0 if not an element
    public int atomicMass;             // 0 if not an element
    public string charge;              // e.g. "+1", "-1", "0", "+2/3"
    public bool isRadioactive;
    public DecayType decayType;        // Enum: None, Alpha, Beta
    public string halfLifeNote;        // display only, not simulated
    public bool isFissile;

    [Header("Harvesting")]
    public bool isHarvested;           // true = raw resource, no recipe
    public FieldType fieldType;        // Enum: None, Positive, Negative, Lepton

    [Header("Secondary Currency")]
    public bool isSecondaryParticle;   // true for alpha/beta particles
    public ParticleUse[] particleUses; // Enum flags: SecondaryParticle, EnergyRecovery, RecipeInput

    [Header("Codex")]
    public string codexEntry;          // educational description
    public bool codexUnlocked;         // runtime — set false by default, unlocked on first craft

    [Header("Economy")]
    public float baseSellValue;        // base currency earned when sold
    public float isotopeSellMultiplier;// multiplier vs base element (isotopes worth more)
}
```

---

## RecipeSO
**Path:** `Assets/ScriptableObjects/Recipes/`
**Naming:** `recipe_{id}.asset` e.g. `recipe_hydrogen.asset`

```csharp
[CreateAssetMenu(menuName = "Game/Recipe")]
public class RecipeSO : ScriptableObject
{
    [Header("Identity")]
    public string id;                       // matches recipes.json id
    public string displayName;

    [Header("Classification")]
    public int tier;
    public RecipeCategory category;         // Enum: Nucleon, Element, Isotope, Molecule, Alloy, Component, Fusion, Fission

    [Header("Inputs")]
    public RecipeIngredient[] inputs;       // see RecipeIngredient struct below
    public ItemSO[] neutronAdjustment;      // for isotope recipes only

    [Header("Output")]
    public ItemSO outputItem;
    public int outputQuantity;

    [Header("Byproducts")]
    public RecipeIngredient[] byproducts;   // e.g. alpha particle from fusion

    [Header("Timing")]
    public float baseCraftTime;             // seconds, from recipes.json
    public float manualCraftTime;           // may differ from building craft time

    [Header("Power")]
    public bool powerCostIsDynamic;         // true = calculated from atomic mass
    public float fixedPowerCostEV;          // used if powerCostIsDynamic = false
    // Dynamic cost calculated at runtime: atomicMass × GameConfig.atomicAssemblerEVPerMassUnit

    [Header("Buildings")]
    public BuildingSO[] validBuildings;     // which buildings can execute this recipe
    public bool canCraftManually;           // available in inventory crafting menu

    [Header("Unlock")]
    public bool knownFromStart;             // false = discovered on first craft
    public ResearchSO requiredResearch;     // null if no research gate
    public ResearchSO unlocksResearch;      // research unlocked when this recipe is first crafted

    [Header("Simplification")]
    public bool isSimplified;               // true if recipe deviates from real science
    public string simplificationNote;       // explains the deviation for dev reference
}

// Inline struct used by RecipeSO
[Serializable]
public struct RecipeIngredient
{
    public ItemSO item;
    public int quantity;
}
```

---

## BuildingSO
**Path:** `Assets/ScriptableObjects/Buildings/`
**Naming:** `building_{id}.asset` e.g. `building_atomic_assembler.asset`

```csharp
[CreateAssetMenu(menuName = "Game/Building")]
public class BuildingSO : ScriptableObject
{
    [Header("Identity")]
    public string id;
    public string displayName;
    public string description;
    public Sprite icon;
    public GameObject prefab;           // 3D model prefab

    [Header("Classification")]
    public int tier;
    public BuildingCategory category;   // Enum: Core, Transient, Power, Megastructure
    public bool isObsoleteable;         // can this building become obsolete?
    public string obsoleteCondition;    // dev note on when it becomes obsolete

    [Header("Power")]
    public bool requiresPower;
    public bool isPowerSource;          // true for generators
    public float basePowerCostEV;       // 0 if powerCostIsDynamic
    public bool powerCostIsDynamic;     // true = driven by active recipe
    public float baseOutputEV;          // for power source buildings only
    public float influenceRadiusTiles;  // power coverage radius
    public float linkRadiusTiles;       // energy grid link radius

    [Header("Production")]
    public float baseOutputRate;        // items per second
    public RecipeSO[] supportedRecipes; // recipes this building can execute
    public int inputSlotCount;          // number of input slots
    public string[] inputSlotLabels;    // e.g. ["Protons", "Neutrons", "Electrons"]

    [Header("Placement")]
    public PlacementRule placementRule; // Enum: Anywhere, MustBeOnField, AdjacentToBuilding
    public BuildingCategory[] compatibleAdjacentCategories;
    public FieldType[] compatibleFields;

    [Header("Upgrades")]
    public BuildingUpgradeLevel[] upgradeLevels; // see struct below

    [Header("Special Upgrade")]
    public bool hasSpecialUpgrade;
    public SpecialUpgradeSO specialUpgrade;      // e.g. Nucleon Harvester merge

    [Header("Unlock")]
    public ResearchSO requiredResearch;  // null if available from start
    public bool availableFromStart;

    [Header("Tutorial")]
    public string tutorialNote;          // dev note for tutorial scripting

    [Header("Decay Collection")]
    public bool collectsDecayParticles;  // true for Radioactive Containment
    public float decayCollectionRate;    // particles per second

    [Header("Codex")]
    public string codexEntry;
}

[Serializable]
public struct BuildingUpgradeLevel
{
    public int level;
    public float outputRate;
    public float powerCostEV;
    public float outputEV;               // for generators
    public float influenceRadiusTiles;   // for generators
    public int costBaseCurrency;
    public int costPrestigeCurrency;     // 0 if not required
}
```

---

## TierSO
**Path:** `Assets/ScriptableObjects/Tiers/`
**Naming:** `tier_{number}_{name}.asset` e.g. `tier_1_subatomic.asset`

```csharp
[CreateAssetMenu(menuName = "Game/Tier")]
public class TierSO : ScriptableObject
{
    public int tierNumber;
    public string displayName;          // e.g. "Subatomic", "Atomic"
    public string description;
    public Color tierColor;             // used for UI color coding
    public Sprite tierIcon;
    public ResearchSO[] unlockResearch; // research required to access this tier
    public ItemSO[] starterItems;       // items visible (but locked) at tier start
}
```

---

## ResearchSO
**Path:** `Assets/ScriptableObjects/Research/`
**Naming:** `research_{id}.asset` e.g. `research_isotopes.asset`

```csharp
[CreateAssetMenu(menuName = "Game/Research")]
public class ResearchSO : ScriptableObject
{
    [Header("Identity")]
    public string id;
    public string displayName;
    public string description;
    public Sprite icon;

    [Header("Tree Position")]
    public ResearchBranch branch;       // Enum: Chemistry, Nuclear, Materials, Engineering, Astrophysics
    public ResearchSO[] prerequisites;  // must be completed before this unlocks
    public int depthInTree;             // used for UI layout

    [Header("Cost")]
    public int costBaseCurrency;
    public int costPrestigeCurrency;    // 0 for most research

    [Header("Unlocks")]
    public ItemSO[] unlocksItems;       // isotopes, ions unlocked by this research
    public RecipeSO[] unlocksRecipes;
    public BuildingSO[] unlocksBuildings;
    public bool unlocksGridExpansion;
    public ResearchSO[] unlocksResearch; // child nodes in tree

    [Header("Prestige Behavior")]
    public bool resetsOnPrestige;       // always true currently
    public float prestigeMemoryDiscount;// % cost reduction on repeat runs (from persistent upgrade)

    [Header("Codex")]
    public string codexEntry;
}
```

---

## GameConfigSO
**Path:** `Assets/ScriptableObjects/Config/`
**Naming:** `game_config.asset` (singleton — one instance)

```csharp
[CreateAssetMenu(menuName = "Game/Config")]
public class GameConfigSO : ScriptableObject
{
    [Header("Craft Time")]
    public float baseCraftTimeMultiplier = 1.0f;
    public float manualCraftTimeBase = 1.0f;

    [Header("Power Economy")]
    public float atomicAssemblerEVPerMassUnit = 5f;
    public float isotopicManipulatorEVPerNeutron = 8f;

    [Header("Prestige")]
    public float netWorthToPrestigeCurrencyRate = 1.0f;
    public float prestigeWallMultiplier = 10.0f;  // how much harder each wall is vs previous

    [Header("Environment")]
    public BuildEnvironment environment; // Enum: Dev, Staging, Prod
    public string apiBaseUrl;            // set per environment at build time

    [Header("Decay Particles")]
    public float alphaParticleEVValue = 20f;  // eV recovered per alpha particle
    public float betaParticleEVValue = 10f;   // eV recovered per beta particle
}
```

---

## FieldSO
**Path:** `Assets/ScriptableObjects/Fields/`
**Naming:** `field_{type}.asset` e.g. `field_positive.asset`

```csharp
[CreateAssetMenu(menuName = "Game/Field")]
public class FieldSO : ScriptableObject
{
    public string id;
    public string displayName;          // e.g. "Positive Quark Field"
    public FieldType fieldType;         // Enum: Positive, Negative, Lepton
    public ItemSO outputItem;           // what this field produces
    public Color fieldColor;            // grid tile tint
    public Sprite fieldIcon;
    public string codexEntry;
}
```

---

## PersistentUpgradeSO
**Path:** `Assets/ScriptableObjects/Upgrades/Persistent/`
**Naming:** `upgrade_persistent_{id}.asset`

```csharp
[CreateAssetMenu(menuName = "Game/Upgrade/Persistent")]
public class PersistentUpgradeSO : ScriptableObject
{
    public string id;
    public string displayName;
    public string description;
    public Sprite icon;

    public UpgradeEffectType effectType; // Enum: CraftSpeedMultiplier, VaultCapacity, ResearchSpeed, DecayCollectionRate, BuildingStartPrePlaced, etc.
    public float effectValue;            // amount of the effect
    public int maxLevel;                 // how many times purchasable
    public int[] costPerLevel;           // prestige currency cost per level

    public PersistentUpgradeSO[] prerequisites;
    public string codexEntry;
}
```

---

## AchievementSO
**Path:** `Assets/ScriptableObjects/Achievements/`
**Naming:** `achievement_{id}.asset`

```csharp
[CreateAssetMenu(menuName = "Game/Achievement")]
public class AchievementSO : ScriptableObject
{
    public string id;
    public string displayName;
    public string description;
    public Sprite icon;
    public bool isHidden;               // hidden until unlocked

    public AchievementTrigger triggerType; // Enum: CraftItem, PlaceBuilding, CompleteResearch, ReachTier, Prestige, WinPVP, UnlockCodex, etc.
    public string triggerTargetId;      // item/building/research id if applicable
    public int triggerQuantity;         // e.g. craft 100 of item

    public CosmeticRewardSO[] rewards;  // cosmetics unlocked on completion
    public string platformAchievementId; // Apple Game Center / Google Play id
}
```

---

## CosmeticSO
**Path:** `Assets/ScriptableObjects/Cosmetics/`
**Naming:** `cosmetic_{id}.asset`

```csharp
[CreateAssetMenu(menuName = "Game/Cosmetic")]
public class CosmeticSO : ScriptableObject
{
    public string id;
    public string displayName;
    public string description;
    public Sprite previewImage;

    public CosmeticType type;           // Enum: BuildingSkin, ParticleEffect, GridTheme, HUDAccent, MusicTrack, CodexCover, ProfileBadge
    public Object cosmeticAsset;        // material, prefab, audio clip, etc.

    public UnlockMethod unlockMethod;   // Enum: Achievement, PremiumCurrency, IAP, Event
    public AchievementSO unlockAchievement;  // if UnlockMethod = Achievement
    public int premiumCurrencyCost;          // if UnlockMethod = PremiumCurrency
    public string iapProductId;              // if UnlockMethod = IAP

    public bool isScreenMusic;          // true if this is a music track cosmetic
    public ScreenTarget musicScreenTarget;   // which screen this music plays on by default
}
```
