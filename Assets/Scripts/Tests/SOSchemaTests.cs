using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Edit Mode tests validating that ScriptableObject types and enums
    /// match the schema definitions in ScriptableObject-Schemas.md.
    /// These tests catch regressions where a field rename or removal would
    /// silently break asset deserialization.
    /// </summary>
    [TestFixture]
    public class SOSchemaTests
    {
        // ── Enum value contracts ──────────────────────────────────────────────

        [Test]
        public void ConditionType_HasExpectedValues()
        {
            Assert.IsTrue(System.Enum.IsDefined(typeof(ConditionType), "Auto"));
            Assert.IsTrue(System.Enum.IsDefined(typeof(ConditionType), "InventoryMin"));
            Assert.IsTrue(System.Enum.IsDefined(typeof(ConditionType), "InventoryZero"));
            Assert.IsTrue(System.Enum.IsDefined(typeof(ConditionType), "UiEvent"));
            Assert.IsTrue(System.Enum.IsDefined(typeof(ConditionType), "ResearchUnlocked"));
            Assert.IsTrue(System.Enum.IsDefined(typeof(ConditionType), "BuildingMin"));
            Assert.IsTrue(System.Enum.IsDefined(typeof(ConditionType), "PrestigeRunMin"));
            Assert.IsTrue(System.Enum.IsDefined(typeof(ConditionType), "PrestigeAvailable"));
        }

        [Test]
        public void BuildingInteractionGate_HasExpectedValues()
        {
            Assert.IsTrue(System.Enum.IsDefined(typeof(BuildingInteractionGate), "None"));
            Assert.IsTrue(System.Enum.IsDefined(typeof(BuildingInteractionGate), "BlockAll"));
            Assert.IsTrue(System.Enum.IsDefined(typeof(BuildingInteractionGate), "EntropySinkOnly"));
        }

        [Test]
        public void TutorialFlowSO_HasStepsField()
        {
            var type = typeof(TutorialFlowSO);
            Assert.IsNotNull(type.GetField("steps"), "TutorialFlowSO missing: steps");
        }

        [Test]
        public void TutorialStepDef_HasRequiredFields()
        {
            var type = typeof(TutorialStepDef);
            Assert.IsNotNull(type.GetField("id"),               "TutorialStepDef missing: id");
            Assert.IsNotNull(type.GetField("hintText"),         "TutorialStepDef missing: hintText");
            Assert.IsNotNull(type.GetField("advanceCondition"), "TutorialStepDef missing: advanceCondition");
            Assert.IsNotNull(type.GetField("onEnter"),          "TutorialStepDef missing: onEnter");
        }

        [Test]
        public void TutorialOnEnter_HasRequiredFields()
        {
            var type = typeof(TutorialOnEnter);
            Assert.IsNotNull(type.GetField("dialogue"),                "TutorialOnEnter missing: dialogue");
            Assert.IsNotNull(type.GetField("highlightTarget"),         "TutorialOnEnter missing: highlightTarget");
            Assert.IsNotNull(type.GetField("blockCollection"),         "TutorialOnEnter missing: blockCollection");
            Assert.IsNotNull(type.GetField("collectionFilter"),        "TutorialOnEnter missing: collectionFilter");
            Assert.IsNotNull(type.GetField("buildingInteractionGate"), "TutorialOnEnter missing: buildingInteractionGate");
        }

        [Test]
        public void ItemCategory_HasExpectedValues()
        {
            Assert.IsTrue(System.Enum.IsDefined(typeof(ItemCategory), "RawResource"));
            Assert.IsTrue(System.Enum.IsDefined(typeof(ItemCategory), "Nucleon"));
            Assert.IsTrue(System.Enum.IsDefined(typeof(ItemCategory), "Element"));
            Assert.IsTrue(System.Enum.IsDefined(typeof(ItemCategory), "Isotope"));
            Assert.IsTrue(System.Enum.IsDefined(typeof(ItemCategory), "Molecule"));
            Assert.IsTrue(System.Enum.IsDefined(typeof(ItemCategory), "Alloy"));
            Assert.IsTrue(System.Enum.IsDefined(typeof(ItemCategory), "Component"));
            Assert.IsTrue(System.Enum.IsDefined(typeof(ItemCategory), "Megastructure"));
        }

        [Test]
        public void RecipeCategory_HasExpectedValues()
        {
            Assert.IsTrue(System.Enum.IsDefined(typeof(RecipeCategory), "Nucleon"));
            Assert.IsTrue(System.Enum.IsDefined(typeof(RecipeCategory), "Fusion"));
            Assert.IsTrue(System.Enum.IsDefined(typeof(RecipeCategory), "Fission"));
        }

        [Test]
        public void FieldType_HasNoneQuarkLepton()
        {
            Assert.IsTrue(System.Enum.IsDefined(typeof(FieldType), "None"));
            Assert.IsTrue(System.Enum.IsDefined(typeof(FieldType), "Quark"));
            Assert.IsTrue(System.Enum.IsDefined(typeof(FieldType), "Lepton"));
        }

        // ── RecipeIngredient struct ───────────────────────────────────────────

        [Test]
        public void RecipeIngredient_CanBeConstructedWithItemAndQuantity()
        {
            var ingredient = new RecipeIngredient
            {
                item = null,   // ItemSO is a Unity object — can't instantiate in Edit Mode
                quantity = 3
            };
            Assert.AreEqual(3, ingredient.quantity);
            Assert.IsNull(ingredient.item);
        }

        // ── ScriptableObject field presence ──────────────────────────────────
        // These use reflection to confirm fields exist with expected names.
        // A field rename will immediately fail the test.

        [Test]
        public void ItemSO_HasRequiredFields()
        {
            var type = typeof(ItemSO);
            Assert.IsNotNull(type.GetField("id"),                   "ItemSO missing: id");
            Assert.IsNotNull(type.GetField("displayName"),          "ItemSO missing: displayName");
            Assert.IsNotNull(type.GetField("symbol"),               "ItemSO missing: symbol");
            Assert.IsNotNull(type.GetField("itemId"),               "ItemSO missing: itemId (ECS int)");
            Assert.IsNotNull(type.GetField("tier"),                 "ItemSO missing: tier");
            Assert.IsNotNull(type.GetField("category"),             "ItemSO missing: category");
            Assert.IsNotNull(type.GetField("atomicNumber"),         "ItemSO missing: atomicNumber");
            Assert.IsNotNull(type.GetField("atomicMass"),           "ItemSO missing: atomicMass");
            Assert.IsNotNull(type.GetField("charge"),               "ItemSO missing: charge");
            Assert.IsNotNull(type.GetField("isRadioactive"),        "ItemSO missing: isRadioactive");
            Assert.IsNotNull(type.GetField("decayType"),            "ItemSO missing: decayType");
            Assert.IsNotNull(type.GetField("isHarvested"),          "ItemSO missing: isHarvested");
            Assert.IsNotNull(type.GetField("codexEntry"),           "ItemSO missing: codexEntry");
            Assert.IsNotNull(type.GetField("codexUnlocked"),        "ItemSO missing: codexUnlocked");
            Assert.IsNotNull(type.GetField("baseSellValue"),        "ItemSO missing: baseSellValue");
        }

        [Test]
        public void RecipeSO_HasRequiredFields()
        {
            var type = typeof(RecipeSO);
            Assert.IsNotNull(type.GetField("id"),                   "RecipeSO missing: id");
            Assert.IsNotNull(type.GetField("recipeId"),             "RecipeSO missing: recipeId (ECS int)");
            Assert.IsNotNull(type.GetField("inputs"),               "RecipeSO missing: inputs");
            Assert.IsNotNull(type.GetField("outputItem"),           "RecipeSO missing: outputItem");
            Assert.IsNotNull(type.GetField("outputQuantity"),       "RecipeSO missing: outputQuantity");
            Assert.IsNotNull(type.GetField("baseCraftTime"),        "RecipeSO missing: baseCraftTime");
            Assert.IsNotNull(type.GetField("manualCraftTime"),      "RecipeSO missing: manualCraftTime");
            Assert.IsNotNull(type.GetField("canCraftManually"),     "RecipeSO missing: canCraftManually");
            Assert.IsNotNull(type.GetField("knownFromStart"),       "RecipeSO missing: knownFromStart");
            Assert.IsNotNull(type.GetField("isSimplified"),         "RecipeSO missing: isSimplified");
        }

        [Test]
        public void BuildingSO_HasRequiredFields()
        {
            var type = typeof(BuildingSO);
            Assert.IsNotNull(type.GetField("id"),                   "BuildingSO missing: id");
            Assert.IsNotNull(type.GetField("buildingId"),           "BuildingSO missing: buildingId (ECS int)");
            Assert.IsNotNull(type.GetField("category"),             "BuildingSO missing: category");
            Assert.IsNotNull(type.GetField("requiresPower"),        "BuildingSO missing: requiresPower");
            Assert.IsNotNull(type.GetField("isPowerSource"),        "BuildingSO missing: isPowerSource");
            Assert.IsNotNull(type.GetField("influenceRadiusTiles"), "BuildingSO missing: influenceRadiusTiles");
            Assert.IsNotNull(type.GetField("supportedRecipes"),     "BuildingSO missing: supportedRecipes");
            Assert.IsNotNull(type.GetField("placementRule"),        "BuildingSO missing: placementRule");
            Assert.IsNotNull(type.GetField("upgradeLevels"),        "BuildingSO missing: upgradeLevels");
            Assert.IsNotNull(type.GetField("availableFromStart"),   "BuildingSO missing: availableFromStart");
            Assert.IsNotNull(type.GetField("collectsDecayParticles"), "BuildingSO missing: collectsDecayParticles");
        }

        [Test]
        public void GameConfigSO_HasRequiredFields()
        {
            var type = typeof(GameConfigSO);
            Assert.IsNotNull(type.GetField("baseCraftTimeMultiplier"),      "GameConfigSO missing: baseCraftTimeMultiplier");
            Assert.IsNotNull(type.GetField("atomicAssemblerEVPerMassUnit"), "GameConfigSO missing: atomicAssemblerEVPerMassUnit");
            Assert.IsNotNull(type.GetField("netWorthToPrestigeCurrencyRate"), "GameConfigSO missing: netWorthToPrestigeCurrencyRate");
            Assert.IsNotNull(type.GetField("alphaParticleEVValue"),         "GameConfigSO missing: alphaParticleEVValue");
            Assert.IsNotNull(type.GetField("startingEntropy"),              "GameConfigSO missing: startingEntropy");
        }

        // ── GameConfigSO defaults ─────────────────────────────────────────────

        [Test]
        public void GameConfigSO_DefaultMultiplierIsOne()
        {
            var config = ScriptableObject.CreateInstance<GameConfigSO>();
            Assert.AreEqual(1.0f, config.baseCraftTimeMultiplier);
            Assert.AreEqual(1.0f, config.netWorthToPrestigeCurrencyRate);
            Object.DestroyImmediate(config);
        }

        [Test]
        public void GameConfigSO_DefaultEVRatesArePositive()
        {
            var config = ScriptableObject.CreateInstance<GameConfigSO>();
            Assert.Greater(config.atomicAssemblerEVPerMassUnit, 0f);
            Assert.Greater(config.alphaParticleEVValue, 0f);
            Assert.Greater(config.betaParticleEVValue, 0f);
            Object.DestroyImmediate(config);
        }

        [Test]
        public void GameConfigSO_DefaultStartingEntropyIsZero()
        {
            var config = ScriptableObject.CreateInstance<GameConfigSO>();
            Assert.AreEqual(0L, config.startingEntropy,
                "Default startingEntropy must be 0 (importer writes the real value from game_data.json)");
            Object.DestroyImmediate(config);
        }

        [Test]
        public void GameConfigSO_StartingEntropyCanBeSet()
        {
            var config = ScriptableObject.CreateInstance<GameConfigSO>();
            config.startingEntropy = 125L;
            Assert.AreEqual(125L, config.startingEntropy);
            Object.DestroyImmediate(config);
        }

        // ── PlayerInventoryAuthoring field contracts ──────────────────────────

        [Test]
        public void PlayerInventoryAuthoring_HasGameConfigField()
        {
            var type = typeof(PlayerInventoryAuthoring);
            Assert.IsNotNull(type.GetField("gameConfig"),       "PlayerInventoryAuthoring missing: gameConfig");
            Assert.IsNotNull(type.GetField("startingEntropy"),  "PlayerInventoryAuthoring missing: startingEntropy");
        }

        [Test]
        public void PlayerInventoryAuthoring_GameConfigFieldIsGameConfigSOType()
        {
            var field = typeof(PlayerInventoryAuthoring).GetField("gameConfig");
            Assert.AreEqual(typeof(GameConfigSO), field.FieldType,
                "gameConfig field must be of type GameConfigSO");
        }
    }
}
