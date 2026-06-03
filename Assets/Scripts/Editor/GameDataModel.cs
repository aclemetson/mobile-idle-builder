using System;
using System.Collections.Generic;
using UnityEngine;

namespace MobileIdleBuilder.Editor
{
    // ─────────────────────────────────────────────────────────────────────────
    // Shared JSON model classes for game_data.json.
    // Used by both GameDataImporter and GameDataEditorWindow.
    // All classes are [Serializable] so JsonUtility can round-trip them.
    // ─────────────────────────────────────────────────────────────────────────

    [Serializable]
    internal class GameDataJson
    {
        public GameConfigJson            game_config     = new();
        public List<TierJson>            tiers           = new();
        public List<ResearchJson>        research        = new();
        public List<ItemJson>            items           = new();
        public List<RecipeJson>          recipes         = new();
        public List<BuildingJson>        buildings       = new();
        public List<FieldJson>           fields          = new();
        public List<DialogueJson>        dialogues       = new();
        public List<TutorialStepJson>    tutorial_steps  = new();

        /// <summary>Ensures no list field is null after deserialization.</summary>
        public void Initialize()
        {
            game_config     ??= new GameConfigJson();
            tiers           ??= new List<TierJson>();
            research        ??= new List<ResearchJson>();
            items           ??= new List<ItemJson>();
            recipes         ??= new List<RecipeJson>();
            buildings       ??= new List<BuildingJson>();
            fields          ??= new List<FieldJson>();
            dialogues       ??= new List<DialogueJson>();
            tutorial_steps  ??= new List<TutorialStepJson>();

            foreach (var r in research)       r?.Initialize();
            foreach (var rec in recipes)      rec?.Initialize();
            foreach (var b in buildings)      b?.Initialize();
            foreach (var f in fields)         f?.Initialize();
            foreach (var d in dialogues)      d?.Initialize();
            foreach (var s in tutorial_steps) s?.Initialize();
        }
    }

    [Serializable]
    internal class GameConfigJson
    {
        public float  base_craft_time_multiplier         = 1f;
        public float  manual_craft_time_base             = 1f;
        public float  atomic_assembler_ev_per_mass_unit  = 5f;
        public float  isotopic_manipulator_ev_per_neutron= 8f;
        public float  net_worth_to_prestige_currency_rate= 1f;
        public float  prestige_base_value                = 5000f;
        public float  prestige_wall_multiplier           = 10f;
        public float  alpha_particle_ev_value            = 20f;
        public float  beta_particle_ev_value             = 10f;
        public string environment                        = "Dev";
        public string api_base_url                       = "TODO";
        public long   starting_entropy                   = 0;
    }

    [Serializable]
    internal class TierJson
    {
        public string   id            = "";
        public int      tier_number   = 1;
        public string   display_name  = "";
        public string   description   = "";
        public string   tier_color    = "#FFFFFF";
        public string   tier_icon_path= "TODO";
        public string[] unlock_research = Array.Empty<string>();
        public string[] starter_items   = Array.Empty<string>();
    }

    [Serializable]
    internal class ResearchJson
    {
        public string   id                     = "";
        public string   display_name           = "";
        public string   description            = "";
        public string   branch                 = "Nuclear";
        public string[] prerequisites          = Array.Empty<string>();
        public int      depth_in_tree          = 0;
        public int      cost_base_currency     = 100;
        public int      cost_prestige_currency = 0;
        public string[] unlocks_items          = Array.Empty<string>();
        public string[] unlocks_recipes        = Array.Empty<string>();
        public string[] unlocks_buildings      = Array.Empty<string>();
        public bool     unlocks_grid_expansion = false;
        public string[] unlocks_research       = Array.Empty<string>();
        public bool     resets_on_prestige     = true;
        public float    prestige_memory_discount = 0.1f;
        public string   codex_entry            = "";
        public string   icon_path              = "TODO";

        public void Initialize()
        {
            prerequisites    ??= Array.Empty<string>();
            unlocks_items    ??= Array.Empty<string>();
            unlocks_recipes  ??= Array.Empty<string>();
            unlocks_buildings??= Array.Empty<string>();
            unlocks_research ??= Array.Empty<string>();
        }
    }

    [Serializable]
    internal class ItemJson
    {
        public string id                    = "";
        public string display_name          = "";
        public string symbol               = "";
        public string icon_path            = "TODO";
        public int    item_id              = 0;
        public int    tier                 = 1;
        public string tier_ref             = "tier_1";
        public string category             = "RawResource";
        public int    atomic_number        = 0;
        public int    atomic_mass          = 0;
        public string charge               = "0";
        public bool   is_radioactive       = false;
        public string decay_type           = "None";
        public string half_life_note       = "";
        public bool   is_fissile           = false;
        public bool   is_harvested         = false;
        public string field_type           = "None";
        public bool   is_secondary_particle= false;
        public int    particle_uses        = 0;
        public string codex_entry          = "";
        public float  base_sell_value      = 1f;
        public float  isotope_sell_multiplier = 1f;
    }

    [Serializable]
    internal class RecipeIngredientJson
    {
        public string item     = "";
        public int    quantity = 1;
    }

    [Serializable]
    internal class RecipeJson
    {
        public string                     id                  = "";
        public string                     display_name        = "";
        public int                        recipe_id           = 0;
        public int                        tier                = 1;
        public string                     category            = "Element";
        public List<RecipeIngredientJson> inputs              = new();
        public string[]                   neutron_adjustment  = Array.Empty<string>();
        public string                     output_item         = "";
        public int                        output_quantity     = 1;
        public List<RecipeIngredientJson> byproducts          = new();
        public float                      base_craft_time     = 1f;
        public float                      manual_craft_time   = 1f;
        public bool                       power_cost_is_dynamic = false;
        public float                      fixed_power_cost_ev = 0f;
        public string[]                   valid_buildings     = Array.Empty<string>();
        public bool                       can_craft_manually  = false;
        public bool                       known_from_start    = false;
        public string                     required_research   = "";
        public string                     unlocks_research    = "";
        public bool                       is_simplified       = false;
        public string                     simplification_note = "";

        public void Initialize()
        {
            inputs             ??= new List<RecipeIngredientJson>();
            byproducts         ??= new List<RecipeIngredientJson>();
            neutron_adjustment ??= Array.Empty<string>();
            valid_buildings    ??= Array.Empty<string>();
        }
    }

    [Serializable]
    internal class UpgradeLevelJson
    {
        public int   level                = 2;
        public float output_rate          = 0f;
        public float power_cost_ev        = 0f;
        public float output_ev            = 0f;
        public float influence_radius_tiles = 0f;
        public int   cost_base_currency   = 0;
        public int   cost_prestige_currency = 0;
    }

    [Serializable]
    internal class PortJson
    {
        public string port_type   = "Input";
        public int[]  local_cell  = { 0, 0 };
        public string local_facing= "West";
    }

    [Serializable]
    internal class BuildingJson
    {
        public string                 id                          = "";
        public string                 display_name               = "";
        public string                 description                = "";
        public string                 icon_path                  = "TODO";
        public string                 prefab_path                = "TODO";
        public int                    building_id                = 0;
        public int                    tier                       = 1;
        public string                 category                   = "Core";
        public bool                   is_obsoleteable            = false;
        public string                 obsolete_condition         = "";
        public bool                   requires_power             = false;
        public bool                   is_power_source            = false;
        public float                  base_power_cost_ev         = 0f;
        public bool                   power_cost_is_dynamic      = false;
        public float                  base_output_ev             = 0f;
        public float                  influence_radius_tiles     = 0f;
        public float                  link_radius_tiles          = 0f;
        public float                  base_output_rate           = 1f;
        public string[]               supported_recipes          = Array.Empty<string>();
        public int                    input_slot_count           = 0;
        public string[]               input_slot_labels          = Array.Empty<string>();
        public bool                   is_entropy_sink            = false;
        public int[]                  footprint                  = { 1, 1 };
        public List<PortJson>         ports                      = new();
        public string                 placement_rule             = "Anywhere";
        public string[]               compatible_adjacent_categories = Array.Empty<string>();
        public string[]               compatible_fields          = Array.Empty<string>();
        public int                    entropy_cost               = 0;
        public List<UpgradeLevelJson> upgrade_levels             = new();
        public bool                   has_special_upgrade        = false;
        public string                 special_upgrade_id         = "";
        public string                 required_research          = "";
        public bool                   available_from_start       = false;
        public string                 tutorial_note              = "";
        public bool                   collects_decay_particles   = false;
        public float                  decay_collection_rate      = 0f;
        public string                 codex_entry                = "";

        public void Initialize()
        {
            supported_recipes          ??= Array.Empty<string>();
            input_slot_labels          ??= Array.Empty<string>();
            footprint                  ??= new[] { 1, 1 };
            ports                      ??= new List<PortJson>();
            compatible_adjacent_categories ??= Array.Empty<string>();
            compatible_fields          ??= Array.Empty<string>();
            upgrade_levels             ??= new List<UpgradeLevelJson>();
        }
    }

    [Serializable]
    internal class FieldDropJson
    {
        public string item   = "";
        public float  weight = 1f;
    }

    [Serializable]
    internal class FieldJson
    {
        public string             id            = "";
        public string             display_name  = "";
        public string             field_type    = "Quark";
        public List<FieldDropJson>drops         = new();
        public float              collection_rate = 1f;
        public string             field_color   = "#FFFFFF";
        public string             field_icon_path = "TODO";
        public string             codex_entry   = "";

        public void Initialize() { drops ??= new List<FieldDropJson>(); }
    }

    [Serializable]
    internal class DialogueLineJson
    {
        public string speaker_name   = "";
        public string text           = "";
        public string portrait_path  = "TODO";
        /// <summary>Freeze Time.timeScale while this line is displayed.</summary>
        public bool   pause_game     = false;
        /// <summary>ID of a UI element or world object to highlight (e.g. "electron_field", "btn-research"). Empty = clear.</summary>
        public string highlight_target = "";
        /// <summary>One-shot directive for TutorialOverlayController (e.g. "pulse_field", "pulse_building"). Empty = none.</summary>
        public string action         = "";
    }

    [Serializable]
    internal class DialogueJson
    {
        public string                  id    = "";
        public List<DialogueLineJson>  lines = new();

        public void Initialize() { lines ??= new List<DialogueLineJson>(); }
    }

    // ── Tutorial step JSON models ─────────────────────────────────────────────

    [Serializable]
    internal class TutorialStepJson
    {
        public string                 id                   = "";
        public string                 hint                 = "";
        public TutorialConditionJson  advance_condition    = new();
        public TutorialSkipJson       skip_condition;
        public TutorialOnEnterJson    on_enter             = new();
        public string[]               locked_research_ids  = new string[0];

        public void Initialize()
        {
            advance_condition    ??= new TutorialConditionJson();
            on_enter             ??= new TutorialOnEnterJson();
            on_enter.locked_messages ??= new List<InteractableMessageJson>();
            locked_research_ids  ??= new string[0];
        }
    }

    [Serializable]
    internal class TutorialConditionJson
    {
        /// <summary>Must match a ConditionType enum value (case-insensitive).</summary>
        public string   type       = "Auto";
        public bool     any_of     = false;
        public List<ItemCountReqJson> items = new();
        public string   ui_event_id  = "";
        public string   research_id  = "";
        public int      min_count    = 0;
    }

    [Serializable]
    internal class ItemCountReqJson
    {
        public int item_id   = 0;
        public int quantity  = 1;
    }

    [Serializable]
    internal class TutorialSkipJson
    {
        public string research_id = "";
        public string skip_to_id  = "";
    }

    [Serializable]
    internal class InteractableMessageJson
    {
        public string trigger_id = "";
        public string message    = "";
        public string icon       = "⚠";
        public string modifier   = "warning";
    }

    [Serializable]
    internal class TutorialOnEnterJson
    {
        public string   dialogue_id              = "";
        public string   highlight_target         = "";
        /// <summary>Must match a HighlightMode enum value (case-insensitive).</summary>
        public string   highlight_mode           = "None";
        public bool     block_collection         = false;
        /// <summary>Must match a FieldType enum value (case-insensitive). "None" = no restriction.</summary>
        public string   collection_filter        = "None";
        public string   pulse_button_id          = "";
        public int[]    demon_highlight_item_ids       = new int[0];
        /// <summary>Must match a BuildingInteractionGate enum value. "None" = no restriction.</summary>
        public string   building_interaction_gate      = "None";
        public List<InteractableMessageJson> locked_messages = new();
    }
}
