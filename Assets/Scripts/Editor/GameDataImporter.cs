using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using MobileIdleBuilder;

namespace MobileIdleBuilder.Editor
{
    /// <summary>
    /// Reads Assets/Data/game_data.json and generates or updates all ScriptableObject assets.
    ///
    /// Run via: MobileIdleBuilder → Import Game Data
    /// Safe to re-run — existing assets are updated in place.
    ///
    /// Generation order (strict dependency chain):
    ///   1. GameConfigSO
    ///   2. TierSO          (pass 1: scalars only)
    ///   3. ResearchSO      (pass 1: scalars only — cross-refs resolved in pass 2)
    ///   4. ItemSO          (resolves tier_ref → TierSO)
    ///   5. RecipeSO        (resolves inputs/output → ItemSO, required_research → ResearchSO)
    ///   6. BuildingSO      (resolves supported_recipes → RecipeSO[], required_research → ResearchSO)
    ///   6.5 Wire RecipeSO.validBuildings (now that BuildingSO exist)
    ///   7. ResearchSO      (pass 2: resolves unlocks_items/recipes/buildings)
    ///   8. FieldSO         (resolves drops → ItemSO)
    ///   9. DialogueSO      (resolves portrait_path → Sprite)
    ///  10. TierSO          (pass 2: resolves unlock_research → ResearchSO[], starter_items → ItemSO[])
    /// </summary>
    public static class GameDataImporter
    {
        internal const string DataPath     = "Assets/Data/game_data.json";
        private const string SettingsDir   = "Assets/Data/settings";
        private const string TiersDir      = "Assets/Data/tiers";
        private const string ResearchDir   = "Assets/Data/research";
        private const string ItemsDir      = "Assets/Resources/Items";
        private const string RecipesDir    = "Assets/Data/recipes";
        private const string BuildingsDir  = "Assets/Data/buildings";
        private const string FieldsDir     = "Assets/Data/fields";
        private const string SitesDir      = "Assets/Data/sites";
        private const string WorldsDir     = "Assets/Data/worlds";
        private const string ManagersDir   = "Assets/Data/managers";
        private const string DialogueDir   = "Assets/Data/dialogue";
        private const string TutorialDir   = "Assets/Data/tutorial";
        private const string ResourcesDir  = "Assets/Resources";

        // ── Entry point ───────────────────────────────────────────────────────

        /// <summary>
        /// Automatically re-runs the importer after every script compilation if:
        /// - any generated asset is missing, OR
        /// - game_data.json has been modified more recently than the last generated asset.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void AutoImportIfMissing()
        {
            bool needsImport = false;

            // Check for missing tutorial flow asset or research database
            if (AssetDatabase.LoadAssetAtPath<TutorialFlowSO>($"{TutorialDir}/tutorial_flow.asset") == null)
                needsImport = true;
            if (AssetDatabase.LoadAssetAtPath<ResearchDatabaseSO>($"{ResourcesDir}/ResearchDatabase.asset") == null)
                needsImport = true;
            if (AssetDatabase.LoadAssetAtPath<SiteDatabaseSO>($"{ResourcesDir}/SiteDatabase.asset") == null)
                needsImport = true;
            if (AssetDatabase.LoadAssetAtPath<WorldDatabaseSO>($"{ResourcesDir}/WorldDatabase.asset") == null)
                needsImport = true;
            if (AssetDatabase.LoadAssetAtPath<BuildingDatabaseSO>($"{ResourcesDir}/BuildingDatabase.asset") == null)
                needsImport = true;
            if (AssetDatabase.LoadAssetAtPath<ManagerDatabaseSO>($"{ResourcesDir}/ManagerDatabase.asset") == null)
                needsImport = true;
            if (AssetDatabase.LoadAssetAtPath<DialogueDatabaseSO>($"{ResourcesDir}/DialogueDatabase.asset") == null)
                needsImport = true;

            // Check if game_data.json is newer than the tutorial flow asset (proxy for last full import)
            if (!needsImport)
            {
                string jsonPath  = $"Assets/Data/game_data.json";
                string assetPath = $"{TutorialDir}/tutorial_flow.asset";
                string jsonFull  = Path.GetFullPath(jsonPath);
                string assetFull = Path.GetFullPath(assetPath);
                if (File.Exists(jsonFull) && File.Exists(assetFull))
                {
                    if (File.GetLastWriteTimeUtc(jsonFull) > File.GetLastWriteTimeUtc(assetFull))
                        needsImport = true;
                }
            }

            if (!needsImport) return;

            // Delay one frame so the AssetDatabase has finished its own post-compile refresh.
            EditorApplication.delayCall += () =>
            {
                var data = LoadJson();
                if (data != null) RunImport(data);
            };
        }

        [MenuItem("MobileIdleBuilder/Import Game Data")]
        public static void Import()
        {
            var data = LoadJson();
            if (data == null) return;
            RunImport(data);
        }

        internal static GameDataJson LoadJson()
        {
            var jsonAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(DataPath);
            if (jsonAsset == null)
            {
                GameLogger.Error($"[GameDataImporter] Could not find {DataPath}");
                return null;
            }
            GameDataJson data;
            try { data = JsonUtility.FromJson<GameDataJson>(jsonAsset.text); }
            catch (Exception e)
            {
                GameLogger.Error($"[GameDataImporter] JSON parse error: {e}");
                return null;
            }
            if (data == null)
            {
                GameLogger.Error("[GameDataImporter] JSON root object was null.");
                return null;
            }
            data.Initialize();
            return data;
        }

        internal static void RunImport(GameDataJson data)
        {
            EnsureDirectory(SettingsDir);
            EnsureDirectory(TiersDir);
            EnsureDirectory(ResearchDir);
            EnsureDirectory(ItemsDir);
            EnsureDirectory(RecipesDir);
            EnsureDirectory(BuildingsDir);
            EnsureDirectory(FieldsDir);
            EnsureDirectory(SitesDir);
            EnsureDirectory(WorldsDir);
            EnsureDirectory(ManagersDir);
            EnsureDirectory(DialogueDir);
            EnsureDirectory(TutorialDir);
            EnsureDirectory(ResourcesDir);

            // ── Step 1: GameConfigSO ─────────────────────────────────────────
            GenerateGameConfig(data.game_config);

            // ── Step 2: TierSO (pass 1 — scalars) ───────────────────────────
            var tierLookup = new Dictionary<string, TierSO>();
            foreach (var t in data.tiers)
                tierLookup[t.id] = GenerateTierPass1(t);

            // ── Step 3: ResearchSO (pass 1 — scalars) ───────────────────────
            var researchLookup = new Dictionary<string, ResearchSO>();
            foreach (var r in data.research)
                researchLookup[r.id] = GenerateResearchPass1(r);

            // ── Step 4: ItemSO ───────────────────────────────────────────────
            var itemLookup = new Dictionary<string, ItemSO>();
            foreach (var item in data.items)
                itemLookup[item.id] = GenerateItem(item, tierLookup);

            // ── Step 5: RecipeSO ─────────────────────────────────────────────
            var recipeLookup = new Dictionary<string, RecipeSO>();
            foreach (var recipe in data.recipes)
                recipeLookup[recipe.id] = GenerateRecipe(recipe, itemLookup, researchLookup);

            // ── Step 6: BuildingSO ───────────────────────────────────────────
            var buildingLookup = new Dictionary<string, BuildingSO>();
            foreach (var building in data.buildings)
                buildingLookup[building.id] = GenerateBuilding(building, recipeLookup, researchLookup);

            // ── Step 6.5: Wire RecipeSO.validBuildings ───────────────────────
            foreach (var recipe in data.recipes)
                if (recipeLookup.TryGetValue(recipe.id, out var recipeSO) && recipe.valid_buildings != null)
                {
                    recipeSO.validBuildings = ResolveArray(recipe.valid_buildings, buildingLookup,
                        $"RecipeSO '{recipe.id}'.validBuildings");
                    EditorUtility.SetDirty(recipeSO);
                }

            // ── Step 7: ResearchSO (pass 2 — cross-refs) ────────────────────
            foreach (var r in data.research)
                if (researchLookup.TryGetValue(r.id, out var so))
                    ResolveResearchCrossRefs(so, r, itemLookup, recipeLookup, buildingLookup, researchLookup);

            // ── Step 8: FieldSO ──────────────────────────────────────────────
            var fieldLookup = new Dictionary<string, FieldSO>();
            foreach (var field in data.fields)
                fieldLookup[field.id] = GenerateField(field, itemLookup);

            // ── Step 8.5: SiteSO (resolves field_overrides → FieldSO) ────────
            var siteLookup = new Dictionary<string, SiteSO>();
            foreach (var site in data.sites)
                siteLookup[site.id] = GenerateSite(site, fieldLookup);

            // ── Step 9: DialogueSO ───────────────────────────────────────────
            var dialogueLookup = new Dictionary<string, DialogueSO>();
            foreach (var dlg in data.dialogues)
            {
                if (string.IsNullOrEmpty(dlg.id)) continue;
                dialogueLookup[dlg.id] = GenerateDialogue(dlg);
            }

            // ── Step 10: TierSO (pass 2 — cross-refs) ───────────────────────
            foreach (var t in data.tiers)
                if (tierLookup.TryGetValue(t.id, out var so))
                    ResolveTierCrossRefs(so, t, researchLookup, itemLookup);

            // ── Step 11: TutorialFlowSO ──────────────────────────────────────
            GenerateTutorialFlow(data.tutorial_steps, dialogueLookup);

            // ── Step 12: ResearchDatabaseSO ──────────────────────────────────
            GenerateResearchDatabase(data.research, researchLookup);

            // ── Step 12.5: SiteDatabaseSO ────────────────────────────────────
            GenerateSiteDatabase(data.sites, siteLookup);

            // ── Step 12.51: WorldSO + WorldDatabaseSO (resolves site_ids / prereq_unlock_ids) ──
            // Worlds cross-ref site ids (from Step 8.5) and prereq research/item ids, so this
            // must run after those lookups are populated.
            var worldPrereqIds = new HashSet<string>();
            foreach (var key in researchLookup.Keys) worldPrereqIds.Add(key);
            foreach (var key in itemLookup.Keys)     worldPrereqIds.Add(key);
            var worldLookup = new Dictionary<string, WorldSO>();
            foreach (var world in data.worlds)
                worldLookup[world.id] = GenerateWorld(world, siteLookup, worldPrereqIds);
            GenerateWorldDatabase(data.worlds, worldLookup);

            // ── Step 12.6: BuildingDatabaseSO (data-driven build menu) ───────
            GenerateBuildingDatabase(data.buildings, buildingLookup);

            // ── Step 12.55: ManagerSO + ManagerDatabaseSO (no cross-refs) ────
            var managerLookup = new Dictionary<string, ManagerSO>();
            foreach (var m in data.managers)
                managerLookup[m.id] = GenerateManager(m);
            GenerateManagerDatabase(data.managers, managerLookup);

            // ── Step 12.6: DialogueDatabaseSO ────────────────────────────────
            GenerateDialogueDatabase(data.dialogues, dialogueLookup);

            // ── Step 13: DailyContentSO (no cross-refs) ──────────────────────
            GenerateDailyContent(data.daily_rewards, data.daily_challenges);

            // ── Step 14: MegastructureSO (resolves stage costs → ItemSO; needs itemLookup) ──
            GenerateMegastructure(data.megastructure, itemLookup);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            GameLogger.Info(
                $"[GameDataImporter] Done — " +
                $"{data.tiers.Count} tiers, {data.research.Count} research, {data.items.Count} items, " +
                $"{data.recipes.Count} recipes, {data.buildings.Count} buildings, " +
                $"{data.fields.Count} fields, {data.sites.Count} sites, {data.worlds.Count} worlds, " +
                $"{data.managers.Count} managers, " +
                $"{data.dialogues.Count} dialogues, " +
                $"{data.tutorial_steps.Count} tutorial steps, " +
                $"{data.daily_rewards.Count} daily rewards, {data.daily_challenges.Count} daily challenges."
            );
        }

        // ── Generators ────────────────────────────────────────────────────────

        private static void GenerateGameConfig(GameConfigJson data)
        {
            if (data == null) return;
            string path = $"{SettingsDir}/game_config.asset";
            var so = LoadOrCreate<GameConfigSO>(path);

            so.baseCraftTimeMultiplier            = data.base_craft_time_multiplier;
            so.manualCraftTimeBase                = data.manual_craft_time_base;
            so.atomicAssemblerEVPerMassUnit       = data.atomic_assembler_ev_per_mass_unit;
            so.isotopicManipulatorEVPerNeutron    = data.isotopic_manipulator_ev_per_neutron;
            so.netWorthToPrestigeCurrencyRate     = data.net_worth_to_prestige_currency_rate;
            so.prestigeBaseValue                  = data.prestige_base_value;
            so.prestigeWallMultiplier             = data.prestige_wall_multiplier;
            so.prestigeCurrencyScale              = data.prestige_currency_scale;
            so.alphaParticleEVValue               = data.alpha_particle_ev_value;
            so.betaParticleEVValue                = data.beta_particle_ev_value;
            so.startingEntropy                    = data.starting_entropy;
            so.buildingPurchaseMultiplierT1       = data.building_purchase_multiplier_t1;
            so.buildingPurchaseMultiplierT2       = data.building_purchase_multiplier_t2;
            so.buildingPurchaseMultiplierT3Plus   = data.building_purchase_multiplier_t3_plus;

            if (TryParseEnum<BuildEnvironment>(data.environment, "GameConfig.environment", out var env))
                so.environment = env;
            if (!IsTodo(data.api_base_url))
                so.apiBaseUrl = data.api_base_url;

            EditorUtility.SetDirty(so);
        }

        private static TierSO GenerateTierPass1(TierJson data)
        {
            string path = $"{TiersDir}/{Sanitize(data.id)}.asset";
            var so = LoadOrCreate<TierSO>(path);

            so.tierNumber  = data.tier_number;
            so.displayName = data.display_name;
            so.description = data.description;

            if (ColorUtility.TryParseHtmlString(data.tier_color, out var color))
                so.tierColor = color;
            so.tierIcon = LoadAssetOrWarn<Sprite>(data.tier_icon_path, $"TierSO '{data.id}'.tierIcon");

            EditorUtility.SetDirty(so);
            return so;
        }

        private static void ResolveTierCrossRefs(TierSO so, TierJson data,
            Dictionary<string, ResearchSO> researchLookup, Dictionary<string, ItemSO> itemLookup)
        {
            so.unlockResearch = ResolveArray(data.unlock_research, researchLookup, $"TierSO '{data.id}'.unlockResearch");
            so.starterItems   = ResolveArray(data.starter_items,   itemLookup,     $"TierSO '{data.id}'.starterItems");
            EditorUtility.SetDirty(so);
        }

        private static ResearchSO GenerateResearchPass1(ResearchJson data)
        {
            string path = $"{ResearchDir}/{Sanitize(data.id)}.asset";
            var so = LoadOrCreate<ResearchSO>(path);

            so.id                   = data.id;
            so.displayName          = data.display_name;
            so.description          = data.description;
            so.depthInTree          = data.depth_in_tree;
            so.costBaseCurrency     = data.cost_base_currency;
            so.costPrestigeCurrency = data.cost_prestige_currency;
            so.durationSeconds      = data.duration_seconds;
            so.fieldCooldownMult    = data.field_cooldown_mult;
            so.unlocksGridExpansion = data.unlocks_grid_expansion;
            so.resetsOnPrestige     = data.resets_on_prestige;
            so.prestigeMemoryDiscount = data.prestige_memory_discount;
            so.codexEntry           = data.codex_entry;

            if (TryParseEnum<ResearchBranch>(data.branch, $"ResearchSO '{data.id}'.branch", out var branch))
                so.branch = branch;
            so.icon = LoadAssetOrWarn<Sprite>(data.icon_path, $"ResearchSO '{data.id}'.icon");

            EditorUtility.SetDirty(so);
            return so;
        }

        private static void ResolveResearchCrossRefs(ResearchSO so, ResearchJson data,
            Dictionary<string, ItemSO> itemLookup, Dictionary<string, RecipeSO> recipeLookup,
            Dictionary<string, BuildingSO> buildingLookup, Dictionary<string, ResearchSO> researchLookup)
        {
            so.prerequisites    = ResolveArray(data.prerequisites,     researchLookup, $"ResearchSO '{data.id}'.prerequisites");
            so.unlocksItems     = ResolveArray(data.unlocks_items,     itemLookup,     $"ResearchSO '{data.id}'.unlocksItems");
            so.unlocksRecipes   = ResolveArray(data.unlocks_recipes,   recipeLookup,   $"ResearchSO '{data.id}'.unlocksRecipes");
            so.unlocksBuildings = ResolveArray(data.unlocks_buildings, buildingLookup, $"ResearchSO '{data.id}'.unlocksBuildings");
            so.unlocksResearch  = ResolveArray(data.unlocks_research,  researchLookup, $"ResearchSO '{data.id}'.unlocksResearch");
            EditorUtility.SetDirty(so);
        }

        private static ItemSO GenerateItem(ItemJson data, Dictionary<string, TierSO> tierLookup)
        {
            string path = $"{ItemsDir}/{Sanitize(data.id)}.asset";
            var so = LoadOrCreate<ItemSO>(path);

            so.id                  = data.id;
            so.displayName         = data.display_name;
            so.symbol              = data.symbol;
            so.itemId              = data.item_id;
            so.tier                = data.tier;
            so.atomicNumber        = data.atomic_number;
            so.atomicMass          = data.atomic_mass;
            so.charge              = data.charge;
            so.isRadioactive       = data.is_radioactive;
            so.halfLifeNote        = data.half_life_note;
            so.isFissile           = data.is_fissile;
            so.isHarvested         = data.is_harvested;
            so.isSecondaryParticle = data.is_secondary_particle;
            so.particleUses        = (ParticleUse)data.particle_uses;
            so.codexEntry          = data.codex_entry;
            so.baseSellValue       = data.base_sell_value;
            so.isotopeSellMultiplier = data.isotope_sell_multiplier;

            so.icon = LoadAssetOrWarn<Sprite>(data.icon_path, $"ItemSO '{data.id}'.icon")
                      ?? LoadGeneratedIcon(data.id);

            if (TryParseEnum<ItemCategory>(data.category, $"ItemSO '{data.id}'.category", out var cat))
                so.category = cat;
            if (TryParseEnum<DecayType>(data.decay_type, $"ItemSO '{data.id}'.decayType", out var decay))
                so.decayType = decay;
            so.fieldType = FieldTypes.Normalize(data.field_type);

            if (!string.IsNullOrEmpty(data.tier_ref) && tierLookup.TryGetValue(data.tier_ref, out var tier))
                so.tierData = tier;
            else if (!string.IsNullOrEmpty(data.tier_ref))
                GameLogger.Warning($"[GameDataImporter] ItemSO '{data.id}': tier_ref '{data.tier_ref}' not found.");

            EditorUtility.SetDirty(so);
            return so;
        }

        private static RecipeSO GenerateRecipe(RecipeJson data,
            Dictionary<string, ItemSO> itemLookup, Dictionary<string, ResearchSO> researchLookup)
        {
            string path = $"{RecipesDir}/{Sanitize(data.id)}.asset";
            var so = LoadOrCreate<RecipeSO>(path);

            so.id               = data.id;
            so.displayName      = data.display_name;
            so.recipeId         = data.recipe_id;
            so.tier             = data.tier;
            so.outputQuantity   = data.output_quantity;
            so.baseCraftTime    = data.base_craft_time;
            so.manualCraftTime  = data.manual_craft_time;
            so.powerCostIsDynamic = data.power_cost_is_dynamic;
            so.fixedPowerCostEV = data.fixed_power_cost_ev;
            so.canCraftManually = data.can_craft_manually;
            so.knownFromStart   = data.known_from_start;
            so.isSimplified     = data.is_simplified;
            so.simplificationNote = data.simplification_note;

            if (TryParseEnum<RecipeCategory>(data.category, $"RecipeSO '{data.id}'.category", out var cat))
                so.category = cat;

            so.outputItem        = ResolveRef(data.output_item,        itemLookup,     $"RecipeSO '{data.id}'.outputItem");
            so.inputs            = BuildIngredients(data.inputs,        itemLookup,     $"RecipeSO '{data.id}'");
            so.byproducts        = BuildIngredients(data.byproducts,    itemLookup,     $"RecipeSO '{data.id}' byproducts");
            so.neutronAdjustment = ResolveArray(data.neutron_adjustment, itemLookup,    $"RecipeSO '{data.id}'.neutronAdjustment");
            so.requiredResearch  = ResolveRef(data.required_research,   researchLookup, $"RecipeSO '{data.id}'.requiredResearch");
            so.unlocksResearch   = ResolveRef(data.unlocks_research,    researchLookup, $"RecipeSO '{data.id}'.unlocksResearch");

            EditorUtility.SetDirty(so);
            return so;
        }

        private static BuildingSO GenerateBuilding(BuildingJson data,
            Dictionary<string, RecipeSO> recipeLookup, Dictionary<string, ResearchSO> researchLookup)
        {
            string path = $"{BuildingsDir}/{Sanitize(data.id)}.asset";
            var so = LoadOrCreate<BuildingSO>(path);

            so.id                  = data.id;
            so.displayName         = data.display_name;
            so.description         = data.description;
            so.buildingId          = data.building_id;
            so.tier                = data.tier;
            so.isObsoleteable      = data.is_obsoleteable;
            so.obsoleteCondition   = data.obsolete_condition;
            so.requiresPower       = data.requires_power;
            so.isPowerSource       = data.is_power_source;
            so.basePowerCostEV     = data.base_power_cost_ev;
            so.powerCostIsDynamic  = data.power_cost_is_dynamic;
            so.baseOutputEV        = data.base_output_ev;
            so.influenceRadiusTiles= data.influence_radius_tiles;
            so.linkRadiusTiles     = data.link_radius_tiles;
            so.baseOutputRate      = data.base_output_rate;
            so.inputSlotCount      = data.input_slot_count;
            so.inputSlotLabels     = data.input_slot_labels ?? Array.Empty<string>();
            so.isEntropySink       = data.is_entropy_sink;
            so.entropyCost                = data.entropy_cost;
            so.baseMaxOutputItems         = data.base_max_output_items;
            so.baseMaxInputItemsPerSlot   = data.base_max_input_items_per_slot;
            so.availableFromStart         = data.available_from_start;
            so.tutorialNote        = data.tutorial_note;
            so.collectsDecayParticles = data.collects_decay_particles;
            so.decayCollectionRate = data.decay_collection_rate;
            so.codexEntry          = data.codex_entry;
            so.hasSpecialUpgrade   = data.has_special_upgrade;

            so.icon   = LoadAssetOrWarn<Sprite>(data.icon_path,    $"BuildingSO '{data.id}'.icon");
            so.prefab = LoadAssetOrWarn<GameObject>(data.prefab_path, $"BuildingSO '{data.id}'.prefab");

            if (TryParseEnum<BuildingCategory>(data.category, $"BuildingSO '{data.id}'.category", out var cat))
                so.category = cat;
            if (TryParseEnum<PlacementRule>(data.placement_rule, $"BuildingSO '{data.id}'.placementRule", out var pr))
                so.placementRule = pr;

            if (data.footprint?.Length >= 2)
                so.footprint = new Vector2Int(data.footprint[0], data.footprint[1]);

            so.structureKind = BuildingStructureKind.None;
            if (!string.IsNullOrEmpty(data.structure_kind) &&
                TryParseEnum<BuildingStructureKind>(data.structure_kind, $"BuildingSO '{data.id}'.structureKind", out var sk))
                so.structureKind = sk;

            so.compatibleAdjacentCategories = ParseEnumArray<BuildingCategory>(
                data.compatible_adjacent_categories, $"BuildingSO '{data.id}'.compatibleAdjacentCategories");
            so.compatibleFields = data.compatible_fields ?? System.Array.Empty<string>();

            if (data.upgrade_levels != null)
            {
                so.upgradeLevels = new BuildingUpgradeLevel[data.upgrade_levels.Count];
                for (int i = 0; i < data.upgrade_levels.Count; i++)
                {
                    var ul = data.upgrade_levels[i];
                    so.upgradeLevels[i] = new BuildingUpgradeLevel
                    {
                        level                = ul.level,
                        outputRate           = ul.output_rate,
                        powerCostEV          = ul.power_cost_ev,
                        outputEV             = ul.output_ev,
                        influenceRadiusTiles = ul.influence_radius_tiles,
                        linkRadiusTiles      = ul.link_radius_tiles,
                        costBaseCurrency     = ul.cost_base_currency,
                        costPrestigeCurrency = ul.cost_prestige_currency
                    };
                }
            }

            if (data.storage_upgrade_levels != null)
            {
                so.storageUpgradeLevels = new BuildingStorageUpgradeLevel[data.storage_upgrade_levels.Count];
                for (int i = 0; i < data.storage_upgrade_levels.Count; i++)
                {
                    var sl = data.storage_upgrade_levels[i];
                    so.storageUpgradeLevels[i] = new BuildingStorageUpgradeLevel
                    {
                        level                = sl.level,
                        maxOutputItems       = sl.max_output_items,
                        costBaseCurrency     = sl.cost_base_currency,
                        costPrestigeCurrency = sl.cost_prestige_currency
                    };
                }
            }

            if (data.input_upgrade_levels != null)
            {
                so.inputUpgradeLevels = new BuildingInputUpgradeLevel[data.input_upgrade_levels.Count];
                for (int i = 0; i < data.input_upgrade_levels.Count; i++)
                {
                    var il = data.input_upgrade_levels[i];
                    so.inputUpgradeLevels[i] = new BuildingInputUpgradeLevel
                    {
                        level                = il.level,
                        maxInputItems        = il.max_input_items,
                        costBaseCurrency     = il.cost_base_currency,
                        costPrestigeCurrency = il.cost_prestige_currency
                    };
                }
            }

            if (data.ports != null)
            {
                so.ports = new BuildingPort[data.ports.Count];
                for (int i = 0; i < data.ports.Count; i++)
                {
                    var pd   = data.ports[i];
                    var port = new BuildingPort();
                    if (TryParseEnum<PortType>(pd.port_type, $"BuildingSO '{data.id}' port[{i}]", out var portType))
                        port.portType = portType;
                    if (pd.local_cell?.Length >= 2)
                        port.localCell = new Vector2Int(pd.local_cell[0], pd.local_cell[1]);
                    if (TryParseEnum<OutputDirection>(pd.local_facing, $"BuildingSO '{data.id}' port[{i}]", out var facing))
                        port.localFacing = facing;
                    so.ports[i] = port;
                }
            }

            so.supportedRecipes = ResolveArray(data.supported_recipes, recipeLookup,   $"BuildingSO '{data.id}'.supportedRecipes");
            so.requiredResearch = ResolveRef(data.required_research,   researchLookup, $"BuildingSO '{data.id}'.requiredResearch");

            EditorUtility.SetDirty(so);
            return so;
        }

        private static FieldSO GenerateField(FieldJson data, Dictionary<string, ItemSO> itemLookup)
        {
            string path = $"{FieldsDir}/{Sanitize(data.id)}.asset";
            var so = LoadOrCreate<FieldSO>(path);

            so.id            = data.id;
            so.displayName   = data.display_name;
            so.tapCooldownSeconds = data.tap_cooldown_seconds;
            so.codexEntry    = data.codex_entry;

            so.fieldType = FieldTypes.Normalize(data.field_type);
            if (ColorUtility.TryParseHtmlString(data.field_color, out var color))
                so.fieldColor = color;
            so.fieldIcon = LoadAssetOrWarn<Sprite>(data.field_icon_path, $"FieldSO '{data.id}'.fieldIcon");

            if (data.drops != null)
            {
                so.drops.Clear();
                foreach (var drop in data.drops)
                    so.drops.Add(new FieldDropEntry
                    {
                        weight = drop.weight,
                        item   = ResolveRef(drop.item, itemLookup, $"FieldSO '{data.id}' drop '{drop.item}'")
                    });
            }

            EditorUtility.SetDirty(so);
            return so;
        }

        private static SiteSO GenerateSite(SiteJson data, Dictionary<string, FieldSO> fieldLookup)
        {
            string path = $"{SitesDir}/{Sanitize(data.id)}.asset";
            var so = LoadOrCreate<SiteSO>(path);

            so.id          = data.id;
            so.displayName = data.display_name;
            so.unlockCost  = data.unlock_cost;

            so.fieldOverrides.Clear();
            if (data.field_overrides != null)
                foreach (var ov in data.field_overrides)
                    so.fieldOverrides.Add(new SiteFieldOverride
                    {
                        field             = ResolveRef(ov.field, fieldLookup, $"SiteSO '{data.id}' field_override '{ov.field}'"),
                        densityMultiplier = ov.density_multiplier
                    });

            EditorUtility.SetDirty(so);
            return so;
        }

        private static ManagerSO GenerateManager(ManagerJson data)
        {
            string path = $"{ManagersDir}/{Sanitize(data.id)}.asset";
            var so = LoadOrCreate<ManagerSO>(path);

            so.id               = data.id;
            so.displayName      = data.display_name;
            so.description      = data.description;
            so.bonusValue       = data.bonus_value;
            so.hireCostPrestige = data.hire_cost_prestige;
            so.starBonusValues  = data.star_bonus_values ?? new[] { data.bonus_value };
            so.starCosts        = data.star_costs        ?? new[] { 0 };

            if (so.starBonusValues.Length != so.starCosts.Length)
                Debug.LogWarning($"ManagerSO '{data.id}': star_bonus_values ({so.starBonusValues.Length}) " +
                                 $"and star_costs ({so.starCosts.Length}) lengths differ; star upgrades may misbehave.");

            if (TryParseEnum<ManagerBonusType>(data.bonus_type, $"ManagerSO '{data.id}'.bonusType", out var bt))
                so.bonusType = bt;
            so.portrait = LoadAssetOrWarn<Sprite>(data.portrait_path, $"ManagerSO '{data.id}'.portrait");

            EditorUtility.SetDirty(so);
            return so;
        }

        private static void GenerateManagerDatabase(List<ManagerJson> managers,
            Dictionary<string, ManagerSO> managerLookup)
        {
            EnsureDirectory(ResourcesDir);
            string path = $"{ResourcesDir}/ManagerDatabase.asset";
            var db = LoadOrCreate<ManagerDatabaseSO>(path);

            var list = new List<ManagerSO>(managers.Count);
            foreach (var m in managers)
                if (managerLookup.TryGetValue(m.id, out var so))
                    list.Add(so);

            db.allManagers = list.ToArray();
            EditorUtility.SetDirty(db);
        }

        private static void GenerateSiteDatabase(List<SiteJson> sites,
            Dictionary<string, SiteSO> siteLookup)
        {
            EnsureDirectory(ResourcesDir);
            string path = $"{ResourcesDir}/SiteDatabase.asset";
            var db = LoadOrCreate<SiteDatabaseSO>(path);

            var list = new System.Collections.Generic.List<SiteSO>(sites.Count);
            foreach (var s in sites)
                if (siteLookup.TryGetValue(s.id, out var so))
                    list.Add(so);

            db.allSites = list.ToArray();
            EditorUtility.SetDirty(db);
        }

        private static WorldSO GenerateWorld(WorldJson data,
            Dictionary<string, SiteSO> siteLookup, HashSet<string> validPrereqIds)
        {
            string path = $"{WorldsDir}/{Sanitize(data.id)}.asset";
            var so = LoadOrCreate<WorldSO>(path);

            so.id          = data.id;
            so.displayName = data.display_name;
            so.unlockCost  = data.unlock_cost;

            so.prereqUnlockIds = new List<string>(data.prereq_unlock_ids ?? new List<string>());
            foreach (var pid in so.prereqUnlockIds)
                if (!string.IsNullOrEmpty(pid) && !validPrereqIds.Contains(pid))
                    GameLogger.Warning($"[GameDataImporter] WorldSO '{data.id}' prereq_unlock_id '{pid}' " +
                                       "matches no research or item id.");

            so.siteIds = new List<string>(data.site_ids ?? new List<string>());
            foreach (var sid in so.siteIds)
                if (!string.IsNullOrEmpty(sid) && !siteLookup.ContainsKey(sid))
                    GameLogger.Warning($"[GameDataImporter] WorldSO '{data.id}' site_id '{sid}' matches no site.");

            // Phase 5: per-world map theme. Empty/invalid hex keeps the WorldTheme default (Physics look).
            so.theme ??= new WorldTheme();
            if (data.theme != null)
            {
                if (ColorUtility.TryParseHtmlString(data.theme.background_color, out var bg))   so.theme.backgroundColor = bg;
                if (ColorUtility.TryParseHtmlString(data.theme.tile_color, out var tile))       so.theme.tileColor       = tile;
                if (ColorUtility.TryParseHtmlString(data.theme.field_tint, out var tint))       so.theme.fieldTint       = tint;
            }

            EditorUtility.SetDirty(so);
            return so;
        }

        private static void GenerateWorldDatabase(List<WorldJson> worlds,
            Dictionary<string, WorldSO> worldLookup)
        {
            EnsureDirectory(ResourcesDir);
            string path = $"{ResourcesDir}/WorldDatabase.asset";
            var db = LoadOrCreate<WorldDatabaseSO>(path);

            var list = new List<WorldSO>(worlds.Count);
            foreach (var w in worlds)
                if (worldLookup.TryGetValue(w.id, out var so))
                    list.Add(so);

            db.allWorlds = list.ToArray();
            EditorUtility.SetDirty(db);
        }

        private static void GenerateBuildingDatabase(List<BuildingJson> buildings,
            Dictionary<string, BuildingSO> buildingLookup)
        {
            EnsureDirectory(ResourcesDir);
            string path = $"{ResourcesDir}/BuildingDatabase.asset";
            var db = LoadOrCreate<BuildingDatabaseSO>(path);

            var list = new System.Collections.Generic.List<BuildingSO>(buildings.Count);
            foreach (var b in buildings)
                if (buildingLookup.TryGetValue(b.id, out var so))
                    list.Add(so);

            db.allBuildings = list.ToArray();
            EditorUtility.SetDirty(db);
        }

        private static void GenerateDialogueDatabase(List<DialogueJson> dialogues,
            Dictionary<string, DialogueSO> dialogueLookup)
        {
            EnsureDirectory(ResourcesDir);
            string path = $"{ResourcesDir}/DialogueDatabase.asset";
            var db = LoadOrCreate<DialogueDatabaseSO>(path);

            var list = new List<DialogueDatabaseSO.Entry>(dialogues?.Count ?? 0);
            if (dialogues != null)
                foreach (var d in dialogues)
                    if (!string.IsNullOrEmpty(d.id) && dialogueLookup.TryGetValue(d.id, out var so))
                        list.Add(new DialogueDatabaseSO.Entry { id = d.id, dialogue = so });

            db.entries = list.ToArray();
            EditorUtility.SetDirty(db);
        }

        private static DialogueSO GenerateDialogue(DialogueJson data)
        {
            string path = $"{DialogueDir}/{Sanitize(data.id)}.asset";
            var so = LoadOrCreate<DialogueSO>(path);

            if (data.lines != null)
            {
                so.lines = new DialogueLine[data.lines.Count];
                for (int i = 0; i < data.lines.Count; i++)
                {
                    var ld = data.lines[i];
                    so.lines[i] = new DialogueLine
                    {
                        speakerName     = ld.speaker_name,
                        text            = IsTodo(ld.text) ? "" : ld.text,
                        portrait        = LoadAssetOrWarn<Sprite>(ld.portrait_path, $"DialogueSO '{data.id}' line[{i}]"),
                        pauseGame       = ld.pause_game,
                        highlightTarget = ld.highlight_target ?? "",
                        action          = ld.action ?? ""
                    };
                }
            }

            EditorUtility.SetDirty(so);
            return so;
        }

        private static void GenerateTutorialFlow(List<TutorialStepJson> steps,
            Dictionary<string, DialogueSO> dialogueLookup)
        {
            if (steps == null || steps.Count == 0) return;

            string path = $"{TutorialDir}/tutorial_flow.asset";
            var so = LoadOrCreate<TutorialFlowSO>(path);

            var realSteps = steps.FindAll(s => !string.IsNullOrEmpty(s.id));
            so.steps = new TutorialStepDef[realSteps.Count];
            for (int i = 0; i < realSteps.Count; i++)
            {
                var s = realSteps[i];
                var def = new TutorialStepDef
                {
                    id       = s.id,
                    hintText = s.hint,
                };

                // Advance condition
                if (s.advance_condition != null)
                {
                    def.advanceCondition = new TutorialConditionDef
                    {
                        anyOf      = s.advance_condition.any_of,
                        uiEventId  = s.advance_condition.ui_event_id  ?? "",
                        researchId = s.advance_condition.research_id  ?? "",
                        minCount   = s.advance_condition.min_count,
                    };
                    if (TryParseEnum<ConditionType>(s.advance_condition.type,
                            $"TutorialStep '{s.id}'.advance_condition.type", out var ct))
                        def.advanceCondition.type = ct;

                    if (s.advance_condition.items != null && s.advance_condition.items.Count > 0)
                    {
                        def.advanceCondition.items = new System.Collections.Generic.List<ItemCountReq>(
                            s.advance_condition.items.Count);
                        foreach (var r in s.advance_condition.items)
                            def.advanceCondition.items.Add(new ItemCountReq
                                { itemId = r.item_id, quantity = r.quantity });
                    }
                }

                // Skip condition
                if (s.skip_condition != null && !string.IsNullOrEmpty(s.skip_condition.research_id))
                {
                    def.skipCondition = new TutorialSkipDef
                    {
                        researchId = s.skip_condition.research_id,
                        skipToId   = s.skip_condition.skip_to_id ?? ""
                    };
                }

                // On-enter actions
                if (s.on_enter != null)
                {
                    def.onEnter = new TutorialOnEnter
                    {
                        highlightTarget       = s.on_enter.highlight_target ?? "",
                        blockCollection       = s.on_enter.block_collection,
                        pulseButtonId         = s.on_enter.pulse_button_id  ?? "",
                        demonHighlightItemIds = s.on_enter.demon_highlight_item_ids,
                    };

                    if (TryParseEnum<HighlightMode>(s.on_enter.highlight_mode,
                            $"TutorialStep '{s.id}'.on_enter.highlight_mode", out var hm))
                        def.onEnter.highlightMode = hm;

                    def.onEnter.collectionFilter = FieldTypes.Normalize(s.on_enter.collection_filter);

                    if (TryParseEnum<BuildingInteractionGate>(s.on_enter.building_interaction_gate,
                            $"TutorialStep '{s.id}'.on_enter.building_interaction_gate", out var big))
                        def.onEnter.buildingInteractionGate = big;

                    if (s.on_enter.locked_messages is { Count: > 0 })
                    {
                        def.onEnter.lockedMessages = new InteractableMessage[s.on_enter.locked_messages.Count];
                        for (int m = 0; m < s.on_enter.locked_messages.Count; m++)
                        {
                            var lm = s.on_enter.locked_messages[m];
                            def.onEnter.lockedMessages[m] = new InteractableMessage
                            {
                                triggerId = lm.trigger_id ?? "",
                                message   = lm.message    ?? "",
                                icon      = string.IsNullOrEmpty(lm.icon) ? "⚠" : lm.icon,
                                modifier  = lm.modifier   ?? "warning"
                            };
                        }
                    }
                    else
                    {
                        def.onEnter.lockedMessages = System.Array.Empty<InteractableMessage>();
                    }

                    if (!string.IsNullOrEmpty(s.on_enter.dialogue_id))
                    {
                        if (dialogueLookup.TryGetValue(s.on_enter.dialogue_id, out var dlg))
                            def.onEnter.dialogue = dlg;
                        else
                            GameLogger.Warning($"[GameDataImporter] TutorialStep '{s.id}': " +
                                             $"dialogue_id '{s.on_enter.dialogue_id}' not found.");
                    }

                }
                else
                {
                    def.onEnter = new TutorialOnEnter();
                }

                def.lockedResearchIds = s.locked_research_ids ?? System.Array.Empty<string>();

                so.steps[i] = def;
            }

            EditorUtility.SetDirty(so);
        }

        private static void GenerateDailyContent(List<DailyRewardJson> rewards,
            List<DailyChallengeJson> challenges)
        {
            EnsureDirectory(ResourcesDir);
            string path = $"{ResourcesDir}/DailyContent.asset";
            var so = LoadOrCreate<DailyContentSO>(path);

            var rewardList = new List<DailyRewardEntry>(rewards?.Count ?? 0);
            if (rewards != null)
                foreach (var r in rewards)
                    rewardList.Add(new DailyRewardEntry
                    {
                        day              = r.day,
                        crystals         = r.crystals,
                        entropy          = r.entropy,
                        prestigeCurrency = r.prestige_currency
                    });
            rewardList.Sort((a, b) => a.day.CompareTo(b.day)); // index by 0-based streak
            so.loginRewards = rewardList.ToArray();

            var challengeList = new List<DailyChallengeEntry>(challenges?.Count ?? 0);
            if (challenges != null)
                foreach (var c in challenges)
                    challengeList.Add(new DailyChallengeEntry
                    {
                        id          = c.id,
                        description = c.description,
                        trigger     = c.trigger,
                        target      = c.target,
                        crystals    = c.crystals
                    });
            so.challengePool = challengeList.ToArray();

            EditorUtility.SetDirty(so);
        }

        private static void GenerateMegastructure(MegastructureJson data,
            Dictionary<string, ItemSO> itemLookup)
        {
            if (data == null || string.IsNullOrEmpty(data.id)) return;

            EnsureDirectory(ResourcesDir);
            string path = $"{ResourcesDir}/Megastructure.asset";
            var so = LoadOrCreate<MegastructureSO>(path);

            so.id               = data.id;
            so.displayName      = data.display_name;
            so.requiredResearch = data.required_research;

            var stages = data.stages ?? new List<MegastructureStageJson>();
            so.stages = new MegastructureStage[stages.Count];
            for (int i = 0; i < stages.Count; i++)
            {
                var sd    = stages[i];
                var costs = sd.costs ?? new List<StageCostJson>();
                var items = new ItemSO[costs.Count];
                var qtys  = new int[costs.Count];
                for (int c = 0; c < costs.Count; c++)
                {
                    items[c] = ResolveRef(costs[c].item, itemLookup, $"Megastructure stage '{sd.id}' cost[{c}]");
                    qtys[c]  = costs[c].quantity;
                }

                var stage = new MegastructureStage
                {
                    id             = sd.id,
                    displayName    = sd.display_name,
                    costItems      = items,
                    costQuantities = qtys,
                    rewardValue    = sd.reward_value,
                };
                if (TryParseEnum<MegastructureRewardType>(sd.reward_type,
                        $"Megastructure stage '{sd.id}'.rewardType", out var rt))
                    stage.rewardType = rt;

                so.stages[i] = stage;
            }

            EditorUtility.SetDirty(so);
        }

        private static void GenerateResearchDatabase(List<ResearchJson> research,
            Dictionary<string, ResearchSO> researchLookup)
        {
            EnsureDirectory(ResourcesDir);
            string path = $"{ResourcesDir}/ResearchDatabase.asset";
            var db = LoadOrCreate<ResearchDatabaseSO>(path);

            var list = new System.Collections.Generic.List<ResearchSO>(research.Count);
            foreach (var r in research)
                if (researchLookup.TryGetValue(r.id, out var so))
                    list.Add(so);

            db.allResearch = list.ToArray();
            EditorUtility.SetDirty(db);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        internal static T LoadOrCreate<T>(string assetPath) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (existing != null) return existing;
            var so = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(so, assetPath);
            return so;
        }

        private static T LoadAssetOrWarn<T>(string path, string context) where T : UnityEngine.Object
        {
            if (IsTodo(path)) return null;
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
                GameLogger.Warning($"[GameDataImporter] {context}: could not load {typeof(T).Name} at '{path}'");
            return asset;
        }

        private static T ResolveRef<T>(string id, Dictionary<string, T> lookup, string context)
            where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (lookup.TryGetValue(id, out var result)) return result;
            GameLogger.Warning($"[GameDataImporter] {context}: ID '{id}' not found.");
            return null;
        }

        private static T[] ResolveArray<T>(string[] ids, Dictionary<string, T> lookup, string context)
            where T : UnityEngine.Object
        {
            if (ids == null || ids.Length == 0) return Array.Empty<T>();
            var list = new List<T>(ids.Length);
            foreach (var id in ids)
            {
                if (string.IsNullOrEmpty(id)) continue;
                if (lookup.TryGetValue(id, out var item)) list.Add(item);
                else GameLogger.Warning($"[GameDataImporter] {context}: ID '{id}' not found.");
            }
            return list.ToArray();
        }

        private static RecipeIngredient[] BuildIngredients(List<RecipeIngredientJson> inputs,
            Dictionary<string, ItemSO> itemLookup, string context)
        {
            if (inputs == null || inputs.Count == 0) return Array.Empty<RecipeIngredient>();
            var result = new RecipeIngredient[inputs.Count];
            for (int i = 0; i < inputs.Count; i++)
                result[i] = new RecipeIngredient
                {
                    item     = ResolveRef(inputs[i].item, itemLookup, $"{context} input[{i}]"),
                    quantity = inputs[i].quantity
                };
            return result;
        }

        private static bool TryParseEnum<TEnum>(string value, string context, out TEnum result)
            where TEnum : struct, Enum
        {
            if (string.IsNullOrEmpty(value)) { result = default; return false; }
            if (Enum.TryParse<TEnum>(value, ignoreCase: true, out result)) return true;
            GameLogger.Warning($"[GameDataImporter] {context}: could not parse '{value}' as {typeof(TEnum).Name}.");
            result = default;
            return false;
        }

        private static TEnum[] ParseEnumArray<TEnum>(string[] values, string context)
            where TEnum : struct, Enum
        {
            if (values == null || values.Length == 0) return Array.Empty<TEnum>();
            var list = new List<TEnum>(values.Length);
            foreach (var v in values)
                if (TryParseEnum<TEnum>(v, context, out var e)) list.Add(e);
            return list.ToArray();
        }

        private static void EnsureDirectory(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string folder = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(folder))
                AssetDatabase.CreateFolder(parent, folder);
        }

        internal static string Sanitize(string name) =>
            name.Replace(" ", "_").Replace("/", "-").Replace("\\", "-");

        internal static bool IsTodo(string value) =>
            string.IsNullOrEmpty(value) || value.Equals("TODO", StringComparison.OrdinalIgnoreCase);

        // Convention fallback: when an item's icon_path is still "TODO", pick up a tile
        // sprite generated by ElementIconGenerator at Assets/Art/Icons/Elements/{id}.png.
        // Durable across re-imports without editing the hand-maintained game_data.json.
        internal const string GeneratedIconsDir = "Assets/Art/Icons/Elements";

        private static Sprite LoadGeneratedIcon(string id) =>
            string.IsNullOrEmpty(id)
                ? null
                : AssetDatabase.LoadAssetAtPath<Sprite>($"{GeneratedIconsDir}/{Sanitize(id)}.png");
    }
}
