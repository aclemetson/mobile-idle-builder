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
        private const string ItemsDir      = "Assets/Data/items";
        private const string RecipesDir    = "Assets/Data/recipes";
        private const string BuildingsDir  = "Assets/Data/buildings";
        private const string FieldsDir     = "Assets/Data/fields";
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
                Debug.LogError($"[GameDataImporter] Could not find {DataPath}");
                return null;
            }
            GameDataJson data;
            try { data = JsonUtility.FromJson<GameDataJson>(jsonAsset.text); }
            catch (Exception e)
            {
                Debug.LogError($"[GameDataImporter] JSON parse error: {e.Message}");
                return null;
            }
            if (data == null)
            {
                Debug.LogError("[GameDataImporter] JSON root object was null.");
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
            foreach (var field in data.fields)
                GenerateField(field, itemLookup);

            // ── Step 9: DialogueSO ───────────────────────────────────────────
            var dialogueLookup = new Dictionary<string, DialogueSO>();
            foreach (var dlg in data.dialogues)
                dialogueLookup[dlg.id] = GenerateDialogue(dlg);

            // ── Step 10: TierSO (pass 2 — cross-refs) ───────────────────────
            foreach (var t in data.tiers)
                if (tierLookup.TryGetValue(t.id, out var so))
                    ResolveTierCrossRefs(so, t, researchLookup, itemLookup);

            // ── Step 11: TutorialFlowSO ──────────────────────────────────────
            GenerateTutorialFlow(data.tutorial_steps, dialogueLookup);

            // ── Step 12: ResearchDatabaseSO ──────────────────────────────────
            GenerateResearchDatabase(data.research, researchLookup);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                $"[GameDataImporter] Done — " +
                $"{data.tiers.Count} tiers, {data.research.Count} research, {data.items.Count} items, " +
                $"{data.recipes.Count} recipes, {data.buildings.Count} buildings, " +
                $"{data.fields.Count} fields, {data.dialogues.Count} dialogues, " +
                $"{data.tutorial_steps.Count} tutorial steps."
            );
        }

        // ── Generators ────────────────────────────────────────────────────────

        private static void GenerateGameConfig(GameConfigJson data)
        {
            if (data == null) return;
            string path = $"{SettingsDir}/game_config.asset";
            var so = LoadOrCreate<GameConfigSO>(path);

            so.baseCraftTimeMultiplier         = data.base_craft_time_multiplier;
            so.manualCraftTimeBase             = data.manual_craft_time_base;
            so.atomicAssemblerEVPerMassUnit    = data.atomic_assembler_ev_per_mass_unit;
            so.isotopicManipulatorEVPerNeutron = data.isotopic_manipulator_ev_per_neutron;
            so.netWorthToPrestigeCurrencyRate  = data.net_worth_to_prestige_currency_rate;
            so.prestigeWallMultiplier          = data.prestige_wall_multiplier;
            so.alphaParticleEVValue            = data.alpha_particle_ev_value;
            so.betaParticleEVValue             = data.beta_particle_ev_value;

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

            so.icon = LoadAssetOrWarn<Sprite>(data.icon_path, $"ItemSO '{data.id}'.icon");

            if (TryParseEnum<ItemCategory>(data.category, $"ItemSO '{data.id}'.category", out var cat))
                so.category = cat;
            if (TryParseEnum<DecayType>(data.decay_type, $"ItemSO '{data.id}'.decayType", out var decay))
                so.decayType = decay;
            if (TryParseEnum<FieldType>(data.field_type, $"ItemSO '{data.id}'.fieldType", out var ft))
                so.fieldType = ft;

            if (!string.IsNullOrEmpty(data.tier_ref) && tierLookup.TryGetValue(data.tier_ref, out var tier))
                so.tierData = tier;
            else if (!string.IsNullOrEmpty(data.tier_ref))
                Debug.LogWarning($"[GameDataImporter] ItemSO '{data.id}': tier_ref '{data.tier_ref}' not found.");

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
            so.entropyCost         = data.entropy_cost;
            so.availableFromStart  = data.available_from_start;
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

            so.compatibleAdjacentCategories = ParseEnumArray<BuildingCategory>(
                data.compatible_adjacent_categories, $"BuildingSO '{data.id}'.compatibleAdjacentCategories");
            so.compatibleFields = ParseEnumArray<FieldType>(
                data.compatible_fields, $"BuildingSO '{data.id}'.compatibleFields");

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
                        costBaseCurrency     = ul.cost_base_currency,
                        costPrestigeCurrency = ul.cost_prestige_currency
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

        private static void GenerateField(FieldJson data, Dictionary<string, ItemSO> itemLookup)
        {
            string path = $"{FieldsDir}/{Sanitize(data.id)}.asset";
            var so = LoadOrCreate<FieldSO>(path);

            so.id            = data.id;
            so.displayName   = data.display_name;
            so.collectionRate= data.collection_rate;
            so.codexEntry    = data.codex_entry;

            if (TryParseEnum<FieldType>(data.field_type, $"FieldSO '{data.id}'.fieldType", out var ft))
                so.fieldType = ft;
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

            so.steps = new TutorialStepDef[steps.Count];
            for (int i = 0; i < steps.Count; i++)
            {
                var s = steps[i];
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

                    if (TryParseEnum<FieldType>(s.on_enter.collection_filter,
                            $"TutorialStep '{s.id}'.on_enter.collection_filter", out var ft))
                        def.onEnter.collectionFilter = ft;

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
                            Debug.LogWarning($"[GameDataImporter] TutorialStep '{s.id}': " +
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
                Debug.LogWarning($"[GameDataImporter] {context}: could not load {typeof(T).Name} at '{path}'");
            return asset;
        }

        private static T ResolveRef<T>(string id, Dictionary<string, T> lookup, string context)
            where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (lookup.TryGetValue(id, out var result)) return result;
            Debug.LogWarning($"[GameDataImporter] {context}: ID '{id}' not found.");
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
                else Debug.LogWarning($"[GameDataImporter] {context}: ID '{id}' not found.");
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
            Debug.LogWarning($"[GameDataImporter] {context}: could not parse '{value}' as {typeof(TEnum).Name}.");
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
    }
}
