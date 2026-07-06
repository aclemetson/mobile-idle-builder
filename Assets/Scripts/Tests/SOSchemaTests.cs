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
            Assert.IsTrue(System.Enum.IsDefined(typeof(BuildingInteractionGate), "AtomicAssemblerOnly"));
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
            Assert.IsTrue(System.Enum.IsDefined(typeof(ItemCategory), "OrganicCompound"));
        }

        [Test]
        public void RecipeCategory_HasExpectedValues()
        {
            Assert.IsTrue(System.Enum.IsDefined(typeof(RecipeCategory), "Nucleon"));
            Assert.IsTrue(System.Enum.IsDefined(typeof(RecipeCategory), "Fusion"));
            Assert.IsTrue(System.Enum.IsDefined(typeof(RecipeCategory), "Fission"));
        }

        [Test]
        public void FieldTypes_UnrestrictedSentinel()
        {
            // Field type is now a free-form string id; "None"/empty/null means "no restriction".
            Assert.IsTrue(FieldTypes.IsUnrestricted(null));
            Assert.IsTrue(FieldTypes.IsUnrestricted(""));
            Assert.IsTrue(FieldTypes.IsUnrestricted("None"));
            Assert.IsTrue(FieldTypes.IsUnrestricted("none"));
            Assert.IsFalse(FieldTypes.IsUnrestricted("Quark"));
            Assert.IsFalse(FieldTypes.IsUnrestricted("Element"));
            Assert.AreEqual("None", FieldTypes.Normalize(null));
            Assert.AreEqual("None", FieldTypes.Normalize(""));
            Assert.AreEqual("Element", FieldTypes.Normalize("Element"));
        }

        [Test]
        public void FieldSO_FieldTypeIsString()
        {
            Assert.AreEqual(typeof(string), typeof(FieldSO).GetField("fieldType").FieldType,
                "FieldSO.fieldType must be a data-driven string id (not an enum)");
            Assert.AreEqual(typeof(string[]), typeof(BuildingSO).GetField("compatibleFields").FieldType,
                "BuildingSO.compatibleFields must be a string[] of field-type ids");
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
        public void BuildingSO_HasUpgradeSystemFields()
        {
            var type = typeof(BuildingSO);
            Assert.IsNotNull(type.GetField("baseMaxOutputItems"),
                "BuildingSO missing: baseMaxOutputItems (output buffer size before stall)");
            Assert.IsNotNull(type.GetField("baseMaxInputItemsPerSlot"),
                "BuildingSO missing: baseMaxInputItemsPerSlot (fixed input buffer)");
            Assert.IsNotNull(type.GetField("storageUpgradeLevels"),
                "BuildingSO missing: storageUpgradeLevels (separate storage upgrade track)");
            Assert.IsNotNull(type.GetField("inputUpgradeLevels"),
                "BuildingSO missing: inputUpgradeLevels (separate input-capacity upgrade track)");
        }

        [Test]
        public void BuildingStorageUpgradeLevel_HasRequiredFields()
        {
            var type = typeof(BuildingStorageUpgradeLevel);
            Assert.IsNotNull(type.GetField("level"),               "BuildingStorageUpgradeLevel missing: level");
            Assert.IsNotNull(type.GetField("maxOutputItems"),      "BuildingStorageUpgradeLevel missing: maxOutputItems");
            Assert.IsNotNull(type.GetField("costBaseCurrency"),    "BuildingStorageUpgradeLevel missing: costBaseCurrency");
            Assert.IsNotNull(type.GetField("costPrestigeCurrency"),"BuildingStorageUpgradeLevel missing: costPrestigeCurrency");
        }

        [Test]
        public void BuildingInputUpgradeLevel_HasRequiredFields()
        {
            var type = typeof(BuildingInputUpgradeLevel);
            Assert.IsNotNull(type.GetField("level"),               "BuildingInputUpgradeLevel missing: level");
            Assert.IsNotNull(type.GetField("maxInputItems"),       "BuildingInputUpgradeLevel missing: maxInputItems");
            Assert.IsNotNull(type.GetField("costBaseCurrency"),    "BuildingInputUpgradeLevel missing: costBaseCurrency");
            Assert.IsNotNull(type.GetField("costPrestigeCurrency"),"BuildingInputUpgradeLevel missing: costPrestigeCurrency");
        }

        [Test]
        public void GameConfigSO_HasRequiredFields()
        {
            var type = typeof(GameConfigSO);
            Assert.IsNotNull(type.GetField("baseCraftTimeMultiplier"),      "GameConfigSO missing: baseCraftTimeMultiplier");
            Assert.IsNotNull(type.GetField("atomicAssemblerEVPerMassUnit"), "GameConfigSO missing: atomicAssemblerEVPerMassUnit");
            Assert.IsNotNull(type.GetField("netWorthToPrestigeCurrencyRate"), "GameConfigSO missing: netWorthToPrestigeCurrencyRate");
            Assert.IsNotNull(type.GetField("alphaParticleEVValue"),         "GameConfigSO missing: alphaParticleEVValue");
        }

        [Test]
        public void GameConfigSO_HasBuildingPurchaseMultiplierFields()
        {
            var type = typeof(GameConfigSO);
            Assert.IsNotNull(type.GetField("buildingPurchaseMultiplierT1"),
                "GameConfigSO missing: buildingPurchaseMultiplierT1");
            Assert.IsNotNull(type.GetField("buildingPurchaseMultiplierT2"),
                "GameConfigSO missing: buildingPurchaseMultiplierT2");
            Assert.IsNotNull(type.GetField("buildingPurchaseMultiplierT3Plus"),
                "GameConfigSO missing: buildingPurchaseMultiplierT3Plus");
        }

        [Test]
        public void GameConfigSO_BuildingPurchaseMultiplierDefaults_AreAboveOne()
        {
            var config = ScriptableObject.CreateInstance<GameConfigSO>();
            Assert.Greater(config.buildingPurchaseMultiplierT1, 1f,
                "T1 multiplier must be > 1 so successive placements cost more");
            Assert.Greater(config.buildingPurchaseMultiplierT2, 1f,
                "T2 multiplier must be > 1");
            Assert.Greater(config.buildingPurchaseMultiplierT3Plus, 1f,
                "T3+ multiplier must be > 1");
            Assert.GreaterOrEqual(config.buildingPurchaseMultiplierT1, config.buildingPurchaseMultiplierT2,
                "T1 multiplier should be ≥ T2 (higher tier = cheaper relative scaling)");
            Assert.GreaterOrEqual(config.buildingPurchaseMultiplierT2, config.buildingPurchaseMultiplierT3Plus,
                "T2 multiplier should be ≥ T3+ (higher tier = cheaper relative scaling)");
            Object.DestroyImmediate(config);
        }

        [Test]
        public void TutorialOverlayController_HasNotifyAtomGeneratorSpeedUpgradedMethod()
        {
            var method = typeof(TutorialOverlayController)
                .GetMethod("NotifyAtomGeneratorSpeedUpgraded",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(method,
                "TutorialOverlayController missing public method: NotifyAtomGeneratorSpeedUpgraded() — needed by the building upgrade UI to advance step 49");
        }

        [Test]
        public void GameConfigSO_HasPrestigeBaseValueField()
        {
            var type = typeof(GameConfigSO);
            Assert.IsNotNull(type.GetField("prestigeBaseValue"),    "GameConfigSO missing: prestigeBaseValue");
            Assert.IsNotNull(type.GetField("prestigeWallMultiplier"), "GameConfigSO missing: prestigeWallMultiplier");
        }

        [Test]
        public void GameConfigSO_PrestigeWallDefaultIs50000()
        {
            var config = ScriptableObject.CreateInstance<GameConfigSO>();
            float wall = config.prestigeBaseValue * config.prestigeWallMultiplier;
            Assert.AreEqual(50000f, wall, 0.001f,
                $"Default prestige wall should be prestigeBaseValue(5000) × prestigeWallMultiplier(10) = 50000, got {wall}");
            Object.DestroyImmediate(config);
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

        // ── SiteSO (multi-grids) ──────────────────────────────────────────────

        [Test]
        public void SiteSO_HasRequiredFields()
        {
            var type = typeof(SiteSO);
            Assert.IsNotNull(type.GetField("id"),             "SiteSO missing: id");
            Assert.IsNotNull(type.GetField("displayName"),    "SiteSO missing: displayName");
            Assert.IsNotNull(type.GetField("unlockCost"),     "SiteSO missing: unlockCost");
            Assert.IsNotNull(type.GetField("fieldOverrides"), "SiteSO missing: fieldOverrides");
        }

        [Test]
        public void SiteFieldOverride_HasRequiredFields()
        {
            var type = typeof(SiteFieldOverride);
            Assert.IsNotNull(type.GetField("field"),             "SiteFieldOverride missing: field");
            Assert.IsNotNull(type.GetField("densityMultiplier"), "SiteFieldOverride missing: densityMultiplier");
        }

        [Test]
        public void SiteSO_DefaultsAreSane()
        {
            var so = ScriptableObject.CreateInstance<SiteSO>();
            Assert.AreEqual(0, so.unlockCost, "Origin-style default unlock cost should be 0");
            Assert.IsNotNull(so.fieldOverrides, "fieldOverrides should be initialized, not null");
            Object.DestroyImmediate(so);
        }

        [Test]
        public void SiteDatabaseSO_HasAllSitesField()
        {
            Assert.IsNotNull(typeof(SiteDatabaseSO).GetField("allSites"),
                "SiteDatabaseSO missing: allSites");
        }

        [Test]
        public void DialogueDatabaseSO_GetReturnsMappedDialogue()
        {
            var dlg = ScriptableObject.CreateInstance<DialogueSO>();
            var db  = ScriptableObject.CreateInstance<DialogueDatabaseSO>();
            db.entries = new[] { new DialogueDatabaseSO.Entry { id = "intro_quantum_domains", dialogue = dlg } };

            Assert.AreSame(dlg, db.Get("intro_quantum_domains"), "Get returns the mapped dialogue");
            Assert.IsNull(db.Get("missing_id"), "unknown id returns null");
            Assert.IsNull(db.Get(null), "null id returns null");

            Object.DestroyImmediate(db);
            Object.DestroyImmediate(dlg);
        }
    }
}
