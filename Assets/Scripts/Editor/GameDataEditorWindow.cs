using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MobileIdleBuilder.Editor
{
    /// <summary>
    /// IMGUI editor window for viewing and editing game_data.json.
    /// Open via: MobileIdleBuilder → Game Data Editor
    ///
    /// Layout: Toolbar → Tabs → Split (List | Detail).
    /// Saving writes back to game_data.json via JsonUtility.
    /// Note: _comment keys in the JSON are documentation-only and are not round-tripped.
    /// </summary>
    public class GameDataEditorWindow : EditorWindow
    {
        // ── State ─────────────────────────────────────────────────────────────

        private GameDataJson _data;
        private bool         _dirty;
        private int          _tab;
        private int          _selectedIndex = -1;
        private Vector2      _listScroll;
        private Vector2      _detailScroll;

        // Cached ID arrays rebuilt whenever data changes
        private string[] _tierIds      = Array.Empty<string>();
        private string[] _researchIds  = Array.Empty<string>();
        private string[] _itemIds      = Array.Empty<string>();
        private string[] _recipeIds    = Array.Empty<string>();
        private string[] _buildingIds  = Array.Empty<string>();

        private static readonly string[] TabNames =
            { "Config", "Tiers", "Research", "Items", "Recipes", "Buildings", "Fields", "Dialogues" };

        private static readonly string[] BranchOptions =
            { "Chemistry", "Nuclear", "Materials", "Engineering", "Astrophysics" };
        private static readonly string[] ItemCategoryOptions =
            { "RawResource", "Nucleon", "Element", "Isotope", "Molecule", "Alloy", "Component", "Particle", "Megastructure" };
        private static readonly string[] RecipeCategoryOptions =
            { "Quark", "Lepton", "Nucleon", "Element", "Isotope", "Molecule", "Alloy", "Component", "Fusion", "Fission" };
        private static readonly string[] BuildingCategoryOptions =
            { "Core", "Transient", "Power", "Megastructure" };
        private static readonly string[] PlacementRuleOptions =
            { "Anywhere", "MustBeOnField", "AdjacentToBuilding" };
        private static readonly string[] FieldTypeOptions =
            { "None", "Quark", "Lepton", "Uranium", "Plutonium", "Element" };
        private static readonly string[] DecayTypeOptions =
            { "None", "Alpha", "Beta" };
        private static readonly string[] EnvironmentOptions =
            { "Dev", "Staging", "Prod" };

        // ── Open / Init ───────────────────────────────────────────────────────

        [MenuItem("MobileIdleBuilder/Game Data Editor")]
        public static void Open()
        {
            var win = GetWindow<GameDataEditorWindow>("Game Data Editor");
            win.minSize = new Vector2(800, 600);
            win.Load();
        }

        private void OnEnable() => Load();

        private void Load()
        {
            _data = GameDataImporter.LoadJson();
            if (_data == null) _data = new GameDataJson();
            _dirty         = false;
            _selectedIndex = -1;
            RebuildIdCaches();
        }

        private void RebuildIdCaches()
        {
            _tierIds     = _data.tiers    .Select(x => x.id).ToArray();
            _researchIds = _data.research .Select(x => x.id).ToArray();
            _itemIds     = _data.items    .Select(x => x.id).ToArray();
            _recipeIds   = _data.recipes  .Select(x => x.id).ToArray();
            _buildingIds = _data.buildings.Select(x => x.id).ToArray();
        }

        // ── Save ──────────────────────────────────────────────────────────────

        private void Save()
        {
            string json = JsonUtility.ToJson(_data, prettyPrint: true);
            // Application.dataPath ends in "/Assets"; go up one level to reach project root
            string projectRoot = Application.dataPath[..^"Assets".Length];
            string fullPath    = projectRoot + GameDataImporter.DataPath;
            File.WriteAllText(fullPath, json);
            AssetDatabase.ImportAsset(GameDataImporter.DataPath);
            _dirty = false;
            GameLogger.Info("[GameDataEditor] Saved game_data.json");
        }

        // ── Root GUI ──────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (_data == null) { EditorGUILayout.HelpBox("No data loaded.", MessageType.Warning); return; }

            DrawToolbar();
            DrawTabs();

            if (_tab == 0)
            {
                // Config has no list — give the detail the full remaining height
                EditorGUI.BeginChangeCheck();
                _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
                DrawConfigDetail();
                EditorGUILayout.EndScrollView();
                if (EditorGUI.EndChangeCheck()) { _dirty = true; RebuildIdCaches(); }
                return;
            }

            // ── List area (full width, fixed height, vertical scroll) ──────────
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.Height(ListAreaHeight()));
            DrawListContent();
            EditorGUILayout.EndScrollView();

            // ── Add / Remove bar ──────────────────────────────────────────────
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("+ Add", EditorStyles.toolbarButton, GUILayout.Width(60))) AddEntry();
            GUI.enabled = _selectedIndex >= 0;
            if (GUILayout.Button("− Remove", EditorStyles.toolbarButton, GUILayout.Width(70)) &&
                EditorUtility.DisplayDialog("Remove", "Remove this entry?", "Remove", "Cancel"))
                RemoveEntry();
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            if (_selectedIndex >= 0)
                GUILayout.Label($"  #{_selectedIndex + 1} selected", EditorStyles.toolbarButton);
            EditorGUILayout.EndHorizontal();

            // ── Detail area (full width, remaining height, vertical scroll) ───
            EditorGUI.BeginChangeCheck();
            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
            DrawDetailContent();
            EditorGUILayout.EndScrollView();
            if (EditorGUI.EndChangeCheck()) { _dirty = true; RebuildIdCaches(); }
        }

        /// <summary>List area height: fits items up to a cap, then scrolls.</summary>
        private float ListAreaHeight()
        {
            int count = GetCurrentList().Count;
            float rowH  = EditorGUIUtility.singleLineHeight + 4;
            float natural = count * rowH + 6;
            float cap   = Mathf.Max(position.height * 0.38f, 120);
            return Mathf.Min(natural, cap);
        }

        // ── Toolbar ───────────────────────────────────────────────────────────

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            GUI.backgroundColor = _dirty ? new Color(1f, 0.6f, 0.3f) : Color.white;
            if (GUILayout.Button(_dirty ? "Save*" : "Save", EditorStyles.toolbarButton, GUILayout.Width(60)))
                Save();
            GUI.backgroundColor = Color.white;

            if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                if (!_dirty || EditorUtility.DisplayDialog("Reload", "Discard unsaved changes?", "Reload", "Cancel"))
                    Load();
            }

            if (GUILayout.Button("Import Assets", EditorStyles.toolbarButton, GUILayout.Width(100)))
            {
                if (_dirty && !EditorUtility.DisplayDialog("Import Assets",
                    "Save before importing?", "Save & Import", "Import Anyway"))
                    Save();
                GameDataImporter.RunImport(_data);
            }

            GUILayout.FlexibleSpace();
            GUI.color = Color.gray;
            GUILayout.Label("Note: _comment keys and tutorial_flow are not round-tripped by this editor.", EditorStyles.toolbarButton);
            GUILayout.Space(8);
            GUILayout.Label(GameDataImporter.DataPath, EditorStyles.toolbarButton);
            GUI.color = Color.white;

            EditorGUILayout.EndHorizontal();
        }

        // ── Tabs ──────────────────────────────────────────────────────────────

        private void DrawTabs()
        {
            int newTab = GUILayout.Toolbar(_tab, TabNames);
            if (newTab != _tab) { _tab = newTab; _selectedIndex = -1; }
        }

        // ── List subtitle helpers ─────────────────────────────────────────────

        private static string ResearchSubtitle(ResearchJson r)
        {
            var unlocks = new List<string>();
            if (r.unlocks_buildings?.Length > 0) unlocks.AddRange(r.unlocks_buildings);
            if (r.unlocks_items?.Length > 0)     unlocks.AddRange(r.unlocks_items);
            // show recipes only when no items listed (avoids duplicating the same names)
            if (r.unlocks_recipes?.Length > 0 && (r.unlocks_items?.Length ?? 0) == 0)
                unlocks.AddRange(r.unlocks_recipes);

            if (unlocks.Count == 0) return $"[{r.branch}] depth {r.depth_in_tree}";
            return $"→ {string.Join(", ", unlocks)}";
        }

        private static string BuildingSubtitle(BuildingJson b)
        {
            var parts = new List<string> { $"Tier {b.tier}", b.category };
            if (b.available_from_start)         parts.Add("start");
            else if (!string.IsNullOrEmpty(b.required_research)) parts.Add(b.required_research);
            if (b.requires_power)               parts.Add("⚡ power");
            if (b.is_power_source)              parts.Add("⚡ source");
            if (b.supported_recipes?.Length > 0) parts.Add($"{b.supported_recipes.Length} recipes");
            return string.Join(" · ", parts);
        }

        // ── List content (full-width rows) ────────────────────────────────────

        private void DrawListContent()
        {
            switch (_tab)
            {
                case 1: DrawList(_data.tiers,     x => x.display_name, x => x.id);           break;
                case 2: DrawList(_data.research,  x => x.display_name, ResearchSubtitle);     break;
                case 3: DrawList(_data.items,     x => x.display_name, x => $"[{x.category}] {x.id}"); break;
                case 4: DrawList(_data.recipes,   x => x.display_name, x => $"Tier {x.tier} · {x.category} · {x.output_item}"); break;
                case 5: DrawList(_data.buildings, x => x.display_name, BuildingSubtitle);     break;
                case 6: DrawList(_data.fields,    x => x.display_name, x => x.field_type);    break;
                case 7: DrawList(_data.dialogues, x => x.id,           x => $"{x.lines?.Count ?? 0} lines"); break;
            }
        }

        private void DrawList<T>(List<T> list, Func<T, string> label, Func<T, string> subtitle)
        {
            var nameStyle = new GUIStyle(EditorStyles.boldLabel) { fixedWidth = 0 };
            var subStyle  = new GUIStyle(EditorStyles.miniLabel)
                { normal = { textColor = EditorGUIUtility.isProSkin ? new Color(0.7f, 0.7f, 0.7f) : new Color(0.35f, 0.35f, 0.35f) } };

            for (int i = 0; i < list.Count; i++)
            {
                bool selected = (i == _selectedIndex);
                if (selected) GUI.backgroundColor = new Color(0.3f, 0.6f, 1f, 0.45f);

                EditorGUILayout.BeginHorizontal(selected ? EditorStyles.helpBox : EditorStyles.label);

                string name = label(list[i]);
                string sub  = subtitle(list[i]);
                GUILayout.Label(string.IsNullOrEmpty(name) ? "(unnamed)" : name, nameStyle, GUILayout.Width(200));
                GUILayout.Label(sub, subStyle);

                EditorGUILayout.EndHorizontal();
                GUI.backgroundColor = Color.white;

                var rect = GUILayoutUtility.GetLastRect();
                if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                {
                    _selectedIndex = i;
                    _detailScroll  = Vector2.zero;
                    Event.current.Use();
                    Repaint();
                }
            }
        }

        // ── Detail content (full-width) ───────────────────────────────────────

        private void DrawDetailContent()
        {
            switch (_tab)
            {
                case 1: if (InRange(_data.tiers))     DrawTierDetail(_data.tiers[_selectedIndex]);          break;
                case 2: if (InRange(_data.research))  DrawResearchDetail(_data.research[_selectedIndex]);   break;
                case 3: if (InRange(_data.items))     DrawItemDetail(_data.items[_selectedIndex]);          break;
                case 4: if (InRange(_data.recipes))   DrawRecipeDetail(_data.recipes[_selectedIndex]);      break;
                case 5: if (InRange(_data.buildings)) DrawBuildingDetail(_data.buildings[_selectedIndex]);  break;
                case 6: if (InRange(_data.fields))    DrawFieldDetail(_data.fields[_selectedIndex]);        break;
                case 7: if (InRange(_data.dialogues)) DrawDialogueDetail(_data.dialogues[_selectedIndex]);  break;
                default:
                    if (_selectedIndex < 0)
                        EditorGUILayout.HelpBox("Select an entry above to edit it.", MessageType.None);
                    break;
            }
        }

        private bool InRange<T>(List<T> list) => _selectedIndex >= 0 && _selectedIndex < list.Count;

        // ── Detail: Config ────────────────────────────────────────────────────

        private void DrawConfigDetail()
        {
            var c = _data.game_config;
            SectionHeader("Game Config");

            H("Craft Time");
            c.base_craft_time_multiplier          = FloatField("Base Craft Time Multiplier", c.base_craft_time_multiplier);
            c.manual_craft_time_base              = FloatField("Manual Craft Time Base",     c.manual_craft_time_base);

            H("Power Economy");
            c.atomic_assembler_ev_per_mass_unit   = FloatField("Assembler eV / Mass Unit",  c.atomic_assembler_ev_per_mass_unit);
            c.isotopic_manipulator_ev_per_neutron = FloatField("Manipulator eV / Neutron",  c.isotopic_manipulator_ev_per_neutron);
            c.alpha_particle_ev_value             = FloatField("Alpha Particle eV Value",   c.alpha_particle_ev_value);
            c.beta_particle_ev_value              = FloatField("Beta Particle eV Value",    c.beta_particle_ev_value);

            H("Prestige");
            c.net_worth_to_prestige_currency_rate = FloatField("Net Worth → Prestige Rate", c.net_worth_to_prestige_currency_rate);
            c.prestige_base_value                 = FloatField("Prestige Base Value",       c.prestige_base_value);
            c.prestige_wall_multiplier            = FloatField("Prestige Wall Multiplier",  c.prestige_wall_multiplier);

            H("Environment");
            c.environment  = StringPopup("Environment", c.environment, EnvironmentOptions);
            c.api_base_url = TextField("API Base URL", c.api_base_url);
        }

        // ── Detail: Tier ──────────────────────────────────────────────────────

        private void DrawTierDetail(TierJson t)
        {
            SectionHeader("Tier");
            t.id           = TextField("ID",           t.id);
            t.tier_number  = IntField("Tier Number",   t.tier_number);
            t.display_name = TextField("Display Name", t.display_name);
            t.description  = TextArea("Description",   t.description);
            t.tier_color   = ColorHexField("Tier Color", t.tier_color);
            t.tier_icon_path = PathField("Icon Path",  t.tier_icon_path);

            H("Cross-References");
            t.unlock_research = IdListField("Unlock Research", t.unlock_research, _researchIds);
            t.starter_items   = IdListField("Starter Items",   t.starter_items,   _itemIds);
        }

        // ── Detail: Research ──────────────────────────────────────────────────

        private void DrawResearchDetail(ResearchJson r)
        {
            SectionHeader("Research Node");
            r.id           = TextField("ID",           r.id);
            r.display_name = TextField("Display Name", r.display_name);
            r.description  = TextArea("Description",   r.description);
            r.branch       = StringPopup("Branch",     r.branch, BranchOptions);
            r.depth_in_tree          = IntField("Depth in Tree",        r.depth_in_tree);
            r.cost_base_currency     = IntField("Cost (Base Currency)", r.cost_base_currency);
            r.cost_prestige_currency = IntField("Cost (Prestige)",      r.cost_prestige_currency);
            r.resets_on_prestige     = Toggle("Resets on Prestige",     r.resets_on_prestige);
            r.prestige_memory_discount = FloatField("Prestige Discount %", r.prestige_memory_discount);
            r.field_cooldown_mult = FloatField("Field Cooldown Mult", r.field_cooldown_mult);
            r.icon_path    = PathField("Icon Path",    r.icon_path);
            r.codex_entry  = TextArea("Codex Entry",   r.codex_entry);

            H("Unlock Dependencies");
            r.prerequisites = IdListField("Prerequisites",   r.prerequisites, _researchIds);

            H("Unlocks");
            r.unlocks_items     = IdListField("Unlocks Items",     r.unlocks_items,    _itemIds);
            r.unlocks_recipes   = IdListField("Unlocks Recipes",   r.unlocks_recipes,  _recipeIds);
            r.unlocks_buildings = IdListField("Unlocks Buildings", r.unlocks_buildings, _buildingIds);
            r.unlocks_research  = IdListField("Unlocks Research",  r.unlocks_research, _researchIds);
            r.unlocks_grid_expansion = Toggle("Unlocks Grid Expansion", r.unlocks_grid_expansion);
        }

        // ── Detail: Item ──────────────────────────────────────────────────────

        private void DrawItemDetail(ItemJson item)
        {
            SectionHeader("Item");
            item.id           = TextField("ID",           item.id);
            item.display_name = TextField("Display Name", item.display_name);
            item.symbol       = TextField("Symbol",        item.symbol);
            item.item_id      = IntField("Item ID (ECS)", item.item_id);
            item.icon_path    = PathField("Icon Path",    item.icon_path);

            H("Classification");
            item.tier     = IntField("Tier",     item.tier);
            item.tier_ref = IdDropdown("Tier",   item.tier_ref, _tierIds);
            item.category = StringPopup("Category", item.category, ItemCategoryOptions);
            item.field_type = StringPopup("Field Type (if harvested)", item.field_type, FieldTypeOptions);

            H("Scientific Data");
            item.atomic_number = IntField("Atomic Number", item.atomic_number);
            item.atomic_mass   = IntField("Atomic Mass",   item.atomic_mass);
            item.charge        = TextField("Charge",        item.charge);
            item.is_radioactive= Toggle("Is Radioactive",   item.is_radioactive);
            if (item.is_radioactive)
            {
                item.decay_type    = StringPopup("Decay Type",    item.decay_type, DecayTypeOptions);
                item.half_life_note= TextField("Half-Life Note",  item.half_life_note);
                item.is_fissile    = Toggle("Is Fissile",          item.is_fissile);
            }

            H("Harvesting / Particles");
            item.is_harvested       = Toggle("Is Harvested (raw resource)", item.is_harvested);
            item.is_secondary_particle = Toggle("Is Secondary Particle", item.is_secondary_particle);
            if (item.is_secondary_particle)
                item.particle_uses = IntField("Particle Uses (bitmask)", item.particle_uses);

            H("Economy");
            item.base_sell_value       = FloatField("Base Sell Value",       item.base_sell_value);
            item.isotope_sell_multiplier = FloatField("Isotope Sell Multiplier", item.isotope_sell_multiplier);

            H("Codex");
            item.codex_entry = TextArea("Codex Entry", item.codex_entry);
        }

        // ── Detail: Recipe ────────────────────────────────────────────────────

        private void DrawRecipeDetail(RecipeJson recipe)
        {
            SectionHeader("Recipe");
            recipe.id           = TextField("ID",           recipe.id);
            recipe.display_name = TextField("Display Name", recipe.display_name);
            recipe.recipe_id    = IntField("Recipe ID (ECS)", recipe.recipe_id);
            recipe.tier         = IntField("Tier",           recipe.tier);
            recipe.category     = StringPopup("Category",   recipe.category, RecipeCategoryOptions);

            H("Output");
            recipe.output_item     = IdDropdown("Output Item", recipe.output_item, _itemIds);
            recipe.output_quantity = IntField("Output Quantity", recipe.output_quantity);

            H("Inputs");
            DrawIngredientList(recipe.inputs, "Inputs", _itemIds);

            H("Byproducts");
            DrawIngredientList(recipe.byproducts, "Byproducts", _itemIds);

            H("Neutron Adjustment");
            recipe.neutron_adjustment = IdListField("Neutron Items", recipe.neutron_adjustment, _itemIds);

            H("Timing");
            recipe.base_craft_time   = FloatField("Base Craft Time (s)",   recipe.base_craft_time);
            recipe.manual_craft_time = FloatField("Manual Craft Time (s)", recipe.manual_craft_time);

            H("Power");
            recipe.power_cost_is_dynamic = Toggle("Power Cost is Dynamic", recipe.power_cost_is_dynamic);
            if (!recipe.power_cost_is_dynamic)
                recipe.fixed_power_cost_ev = FloatField("Fixed Power Cost (eV)", recipe.fixed_power_cost_ev);

            H("Availability");
            recipe.can_craft_manually = Toggle("Can Craft Manually", recipe.can_craft_manually);
            recipe.known_from_start   = Toggle("Known From Start",   recipe.known_from_start);
            recipe.required_research  = IdDropdown("Required Research", recipe.required_research, _researchIds, allowNone: true);
            recipe.unlocks_research   = IdDropdown("Unlocks Research",  recipe.unlocks_research,  _researchIds, allowNone: true);

            H("Valid Buildings");
            recipe.valid_buildings = IdListField("Valid Buildings", recipe.valid_buildings, _buildingIds);

            H("Simplification");
            recipe.is_simplified     = Toggle("Is Simplified", recipe.is_simplified);
            if (recipe.is_simplified)
                recipe.simplification_note = TextArea("Simplification Note", recipe.simplification_note);
        }

        // ── Detail: Building ──────────────────────────────────────────────────

        private void DrawBuildingDetail(BuildingJson b)
        {
            SectionHeader("Building");
            b.id           = TextField("ID",           b.id);
            b.display_name = TextField("Display Name", b.display_name);
            b.description  = TextArea("Description",   b.description);
            b.building_id  = IntField("Building ID (ECS)", b.building_id);
            b.tier         = IntField("Tier",          b.tier);
            b.category     = StringPopup("Category",   b.category, BuildingCategoryOptions);
            b.icon_path    = PathField("Icon Path",    b.icon_path);
            b.prefab_path  = PathField("Prefab Path",  b.prefab_path);

            H("Obsolescence");
            b.is_obsoleteable    = Toggle("Is Obsoleteable", b.is_obsoleteable);
            if (b.is_obsoleteable)
                b.obsolete_condition = TextArea("Obsolete Condition", b.obsolete_condition);

            H("Power");
            b.requires_power       = Toggle("Requires Power",        b.requires_power);
            b.is_power_source      = Toggle("Is Power Source",        b.is_power_source);
            b.power_cost_is_dynamic= Toggle("Power Cost is Dynamic", b.power_cost_is_dynamic);
            if (!b.power_cost_is_dynamic)
                b.base_power_cost_ev = FloatField("Base Power Cost (eV)", b.base_power_cost_ev);
            if (b.is_power_source)
            {
                b.base_output_ev         = FloatField("Base Output (eV)",         b.base_output_ev);
                b.influence_radius_tiles = FloatField("Influence Radius (tiles)", b.influence_radius_tiles);
                b.link_radius_tiles      = FloatField("Link Radius (tiles)",      b.link_radius_tiles);
            }

            H("Production");
            b.base_output_rate = FloatField("Base Output Rate (items/s)", b.base_output_rate);
            b.input_slot_count = IntField("Input Slot Count", b.input_slot_count);
            b.input_slot_labels= StringArrayField("Input Slot Labels", b.input_slot_labels);
            b.is_entropy_sink  = Toggle("Is Entropy Sink", b.is_entropy_sink);

            H("Supported Recipes");
            b.supported_recipes = IdListField("Recipes", b.supported_recipes, _recipeIds);

            H("Placement");
            b.placement_rule   = StringPopup("Placement Rule", b.placement_rule, PlacementRuleOptions);
            b.compatible_fields= IdListField("Compatible Fields",      b.compatible_fields, FieldTypeOptions);
            b.compatible_adjacent_categories = IdListField("Compatible Adjacent Categories",
                b.compatible_adjacent_categories, BuildingCategoryOptions);
            b.entropy_cost     = IntField("Entropy Cost", b.entropy_cost);

            // Footprint
            if (b.footprint == null || b.footprint.Length < 2) b.footprint = new[] { 1, 1 };
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Footprint (W × H)");
            b.footprint[0] = EditorGUILayout.IntField(b.footprint[0], GUILayout.Width(40));
            GUILayout.Label("×", GUILayout.Width(14));
            b.footprint[1] = EditorGUILayout.IntField(b.footprint[1], GUILayout.Width(40));
            EditorGUILayout.EndHorizontal();

            H("Upgrade Levels");
            DrawUpgradeLevels(b.upgrade_levels);

            H("Special Upgrade");
            b.has_special_upgrade = Toggle("Has Special Upgrade", b.has_special_upgrade);
            if (b.has_special_upgrade)
                b.special_upgrade_id = TextField("Special Upgrade ID", b.special_upgrade_id);

            H("Unlock");
            b.required_research  = IdDropdown("Required Research", b.required_research, _researchIds, allowNone: true);
            b.available_from_start = Toggle("Available From Start", b.available_from_start);

            H("Tutorial");
            b.tutorial_note = TextArea("Tutorial Note", b.tutorial_note);

            H("Decay Collection");
            b.collects_decay_particles = Toggle("Collects Decay Particles", b.collects_decay_particles);
            if (b.collects_decay_particles)
                b.decay_collection_rate = FloatField("Decay Collection Rate", b.decay_collection_rate);

            H("Codex");
            b.codex_entry = TextArea("Codex Entry", b.codex_entry);
        }

        // ── Detail: Field ─────────────────────────────────────────────────────

        private void DrawFieldDetail(FieldJson f)
        {
            SectionHeader("Field");
            f.id           = TextField("ID",           f.id);
            f.display_name = TextField("Display Name", f.display_name);
            f.field_type   = StringPopup("Field Type", f.field_type, FieldTypeOptions);
            f.tap_cooldown_seconds = FloatField("Tap Cooldown (s)", f.tap_cooldown_seconds);
            f.field_color  = ColorHexField("Field Color", f.field_color);
            f.field_icon_path = PathField("Icon Path", f.field_icon_path);

            H("Drop Table");
            DrawFieldDrops(f.drops);

            H("Codex");
            f.codex_entry = TextArea("Codex Entry", f.codex_entry);
        }

        // ── Detail: Dialogue ──────────────────────────────────────────────────

        private void DrawDialogueDetail(DialogueJson dlg)
        {
            SectionHeader("Dialogue");
            dlg.id = TextField("ID", dlg.id);

            H("Lines");
            for (int i = 0; i < dlg.lines.Count; i++)
            {
                var line = dlg.lines[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label($"Line {i + 1}", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (i > 0 && GUILayout.Button("▲", GUILayout.Width(24)))
                { dlg.lines.RemoveAt(i); dlg.lines.Insert(i - 1, line); _dirty = true; break; }
                if (i < dlg.lines.Count - 1 && GUILayout.Button("▼", GUILayout.Width(24)))
                { dlg.lines.RemoveAt(i); dlg.lines.Insert(i + 1, line); _dirty = true; break; }
                if (GUILayout.Button("✕", GUILayout.Width(24)))
                { dlg.lines.RemoveAt(i); _dirty = true; break; }
                EditorGUILayout.EndHorizontal();

                line.speaker_name  = TextField("Speaker",      line.speaker_name);
                line.text          = TextArea("Text",           line.text, minLines: 3);
                line.portrait_path = PathField("Portrait Path", line.portrait_path);
                EditorGUILayout.EndVertical();
                GUILayout.Space(4);
            }

            if (GUILayout.Button("+ Add Line"))
                dlg.lines.Add(new DialogueLineJson());
        }

        // ── Sub-editors ───────────────────────────────────────────────────────

        private void DrawIngredientList(List<RecipeIngredientJson> list, string label, string[] idOptions)
        {
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            for (int i = 0; i < list.Count; i++)
            {
                var ing = list[i];
                EditorGUILayout.BeginHorizontal();
                ing.item     = IdDropdown("", ing.item, idOptions, labelWidth: 0);
                ing.quantity = EditorGUILayout.IntField(ing.quantity, GUILayout.Width(50));
                if (GUILayout.Button("✕", GUILayout.Width(22)))
                { list.RemoveAt(i); _dirty = true; break; }
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button($"+ Add to {label}"))
                list.Add(new RecipeIngredientJson());
            GUILayout.Space(4);
        }

        private void DrawUpgradeLevels(List<UpgradeLevelJson> levels)
        {
            for (int i = 0; i < levels.Count; i++)
            {
                var ul = levels[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label($"Level {ul.level}", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("✕", GUILayout.Width(22)))
                { levels.RemoveAt(i); _dirty = true; break; }
                EditorGUILayout.EndHorizontal();

                ul.level                = IntField("Level",                 ul.level);
                ul.output_rate          = FloatField("Output Rate",         ul.output_rate);
                ul.power_cost_ev        = FloatField("Power Cost (eV)",     ul.power_cost_ev);
                ul.output_ev            = FloatField("Output EV",           ul.output_ev);
                ul.influence_radius_tiles = FloatField("Influence Radius",  ul.influence_radius_tiles);
                ul.cost_base_currency   = IntField("Cost (Base Currency)",  ul.cost_base_currency);
                ul.cost_prestige_currency = IntField("Cost (Prestige)",     ul.cost_prestige_currency);
                EditorGUILayout.EndVertical();
                GUILayout.Space(2);
            }
            if (GUILayout.Button("+ Add Upgrade Level"))
            {
                int nextLevel = levels.Count > 0 ? levels[^1].level + 1 : 2;
                levels.Add(new UpgradeLevelJson { level = nextLevel });
            }
        }

        private void DrawFieldDrops(List<FieldDropJson> drops)
        {
            for (int i = 0; i < drops.Count; i++)
            {
                var drop = drops[i];
                EditorGUILayout.BeginHorizontal();
                drop.item   = IdDropdown("", drop.item, _itemIds, labelWidth: 0);
                GUILayout.Label("weight", GUILayout.Width(44));
                drop.weight = EditorGUILayout.FloatField(drop.weight, GUILayout.Width(50));
                if (GUILayout.Button("✕", GUILayout.Width(22)))
                { drops.RemoveAt(i); _dirty = true; break; }
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("+ Add Drop"))
                drops.Add(new FieldDropJson());
        }

        // ── Add / Remove entries ──────────────────────────────────────────────

        private void AddEntry()
        {
            switch (_tab)
            {
                case 1: _data.tiers    .Add(new TierJson     { id = "new_tier" });     break;
                case 2: _data.research .Add(new ResearchJson { id = "new_research" }); break;
                case 3: _data.items    .Add(new ItemJson     { id = "new_item" });     break;
                case 4: _data.recipes  .Add(new RecipeJson   { id = "new_recipe" });   break;
                case 5: _data.buildings.Add(new BuildingJson { id = "new_building" }); break;
                case 6: _data.fields   .Add(new FieldJson    { id = "new_field" });    break;
                case 7: _data.dialogues.Add(new DialogueJson { id = "new_dialogue" }); break;
            }
            _selectedIndex = GetCurrentList().Count - 1;
            _dirty = true;
            RebuildIdCaches();
        }

        private void RemoveEntry()
        {
            var list = GetCurrentList();
            if (_selectedIndex < 0 || _selectedIndex >= list.Count) return;
            list.RemoveAt(_selectedIndex);
            _selectedIndex = Mathf.Clamp(_selectedIndex - 1, 0, list.Count - 1);
            if (list.Count == 0) _selectedIndex = -1;
            _dirty = true;
            RebuildIdCaches();
        }

        private System.Collections.IList GetCurrentList() => _tab switch
        {
            1 => _data.tiers,
            2 => _data.research,
            3 => _data.items,
            4 => _data.recipes,
            5 => _data.buildings,
            6 => _data.fields,
            7 => _data.dialogues,
            _ => new List<object>()
        };

        // ── UI helpers ────────────────────────────────────────────────────────

        private static void SectionHeader(string title)
        {
            GUILayout.Space(4);
            var style = new GUIStyle(EditorStyles.boldLabel)
                { fontSize = 13, normal = { textColor = EditorGUIUtility.isProSkin ? Color.white : Color.black } };
            GUILayout.Label(title, style);
            GUILayout.Space(4);
        }

        private static void H(string header) // sub-section header
        {
            GUILayout.Space(6);
            EditorGUILayout.LabelField(header, EditorStyles.boldLabel);
        }

        private static string TextField(string label, string value, float labelWidth = 160)
        {
            EditorGUILayout.BeginHorizontal();
            if (labelWidth > 0) EditorGUILayout.LabelField(label, GUILayout.Width(labelWidth));
            string result = EditorGUILayout.TextField(value ?? "");
            EditorGUILayout.EndHorizontal();
            return result;
        }

        private static string TextArea(string label, string value, int minLines = 2)
        {
            EditorGUILayout.LabelField(label, EditorStyles.miniLabel);
            return EditorGUILayout.TextArea(value ?? "", GUILayout.MinHeight(EditorGUIUtility.singleLineHeight * minLines));
        }

        private static int IntField(string label, int value, float labelWidth = 160)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(labelWidth));
            int result = EditorGUILayout.IntField(value);
            EditorGUILayout.EndHorizontal();
            return result;
        }

        private static float FloatField(string label, float value, float labelWidth = 160)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(labelWidth));
            float result = EditorGUILayout.FloatField(value);
            EditorGUILayout.EndHorizontal();
            return result;
        }

        private static bool Toggle(string label, bool value, float labelWidth = 160)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(labelWidth));
            bool result = EditorGUILayout.Toggle(value);
            EditorGUILayout.EndHorizontal();
            return result;
        }

        private static string StringPopup(string label, string value, string[] options, float labelWidth = 160)
        {
            int idx = Mathf.Max(0, Array.IndexOf(options, value));
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(labelWidth));
            idx = EditorGUILayout.Popup(idx, options);
            EditorGUILayout.EndHorizontal();
            return options[idx];
        }

        private static string PathField(string label, string value, float labelWidth = 160)
        {
            bool isTodo = GameDataImporter.IsTodo(value);
            if (isTodo) GUI.color = new Color(1f, 0.6f, 0.3f);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(labelWidth));
            string result = EditorGUILayout.TextField(value ?? "TODO");
            if (GUILayout.Button("…", GUILayout.Width(24)))
            {
                string path = EditorUtility.OpenFilePanel("Select Asset", "Assets", "");
                if (!string.IsNullOrEmpty(path))
                {
                    // Make path relative to project root
                    string proj = Path.GetFullPath(Application.dataPath + "/..").Replace('\\', '/');
                    path = path.Replace('\\', '/');
                    if (path.StartsWith(proj)) path = path.Substring(proj.Length + 1);
                    result = path;
                }
            }
            EditorGUILayout.EndHorizontal();
            GUI.color = Color.white;
            return result;
        }

        private static string ColorHexField(string label, string hexValue, float labelWidth = 160)
        {
            Color color = Color.white;
            ColorUtility.TryParseHtmlString(hexValue, out color);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(labelWidth));
            Color newColor = EditorGUILayout.ColorField(color);
            EditorGUILayout.EndHorizontal();

            return "#" + ColorUtility.ToHtmlStringRGB(newColor);
        }

        /// <summary>Dropdown that selects one ID from a fixed list. allowNone adds a "(none)" entry.</summary>
        private static string IdDropdown(string label, string currentId, string[] options,
            bool allowNone = false, float labelWidth = 160)
        {
            string[] display = allowNone
                ? new[] { "(none)" }.Concat(options).ToArray()
                : options.Length > 0 ? options : new[] { "(none)" };

            int offset = allowNone ? 1 : 0;
            int idx    = Array.IndexOf(options, currentId);
            int dispIdx = allowNone ? idx + 1 : Mathf.Max(0, idx);

            EditorGUILayout.BeginHorizontal();
            if (labelWidth > 0) EditorGUILayout.LabelField(label, GUILayout.Width(labelWidth));
            dispIdx = EditorGUILayout.Popup(dispIdx, display);
            EditorGUILayout.EndHorizontal();

            int resultIdx = dispIdx - offset;
            return (resultIdx >= 0 && resultIdx < options.Length) ? options[resultIdx] : "";
        }

        /// <summary>Editable list of string IDs chosen from a dropdown of valid values.</summary>
        private static string[] IdListField(string label, string[] ids, string[] validOptions)
        {
            ids ??= Array.Empty<string>();
            string[] allOptions  = new[] { "(none)" }.Concat(validOptions).ToArray();

            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            var result = new List<string>(ids);
            int removeAt = -1;

            for (int i = 0; i < result.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                int idx    = Array.IndexOf(validOptions, result[i]);
                int dispIdx= idx + 1; // offset for "(none)"
                dispIdx    = EditorGUILayout.Popup(dispIdx, allOptions);
                result[i]  = dispIdx > 0 ? validOptions[dispIdx - 1] : "";

                if (GUILayout.Button("✕", GUILayout.Width(22))) removeAt = i;
                EditorGUILayout.EndHorizontal();
            }

            if (removeAt >= 0) result.RemoveAt(removeAt);

            if (GUILayout.Button($"+ {label}", GUILayout.Width(200)))
                result.Add(validOptions.Length > 0 ? validOptions[0] : "");

            return result.ToArray();
        }

        /// <summary>Editable string array (e.g. input slot labels).</summary>
        private static string[] StringArrayField(string label, string[] values)
        {
            values ??= Array.Empty<string>();
            var result   = new List<string>(values);
            int removeAt = -1;

            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            for (int i = 0; i < result.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                result[i] = EditorGUILayout.TextField(result[i]);
                if (GUILayout.Button("✕", GUILayout.Width(22))) removeAt = i;
                EditorGUILayout.EndHorizontal();
            }
            if (removeAt >= 0) result.RemoveAt(removeAt);
            if (GUILayout.Button($"+ {label}", GUILayout.Width(200)))
                result.Add("");
            return result.ToArray();
        }
    }
}
