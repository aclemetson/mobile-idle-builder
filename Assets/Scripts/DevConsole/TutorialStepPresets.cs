#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;

namespace MobileIdleBuilder.Dev
{
    /// <summary>
    /// Static preset data used by the "tutorial skip" dev console command.
    /// Entropy values are derived from gameplay-loop.html economy documentation.
    /// Building/conveyor presets for grid checkpoints (steps 31+) start as null
    /// until captured from a real playthrough — see comments below for how to fill them in.
    /// </summary>
    internal static class TutorialStepPresets
    {
        // ── Entropy lookup ────────────────────────────────────────────────────
        // Maps step ID → entropy to grant when skipping TO that step.
        // GetEntropy walks backward from stepIndex to find the nearest preceding entry.
        // All values derived from gameplay-loop.html economy notes.

        static readonly Dictionary<string, long> s_EntropyTable = new()
        {
            { "intro_dialogue",               250 },
            { "buy_recombination_i",          265 },   // enough to afford 100e purchase
            { "recombination_unlocked",       165 },   // after -100e
            { "collect_quarks_for_nucleons",  165 },
            { "buy_hydrogen_synthesis",       177 },   // enough to afford 50e purchase
            { "gather_for_hydrogen",          127 },   // after -50e
            { "buy_automation_i",             137 },   // enough to afford 25e purchase
            { "place_first_building",         112 },   // after -25e; enough for 100e Harvester
            { "place_conveyor",                12 },   // after -100e Harvester
            { "automation_started",            12 },
            { "intro_sfc",                     50 },   // idle income accumulated
            { "place_sfc",                    200 },   // enough for 200e SFC
            { "place_generator",                0 },   // after -200e; need to idle for Generator
            { "link_generator",               150 },   // enough for 150e Generator
            { "place_quark_harvester",          0 },   // after -150e; need to idle for 2nd Harvester
            { "route_quarks_to_sfc",           50 },
            { "configure_sfc_recipes",         50 },
            { "route_nucleons_to_demon",        50 },
            { "nucleon_loop_complete",         100 },
            { "intro_atom_gen",               350 },   // enough for 350e Atom Generator
            { "place_atom_generator",         350 },
            { "connect_atom_gen_inputs",        50 },
            { "route_hydrogen_to_demon",        50 },
            { "hydrogen_loop_complete",         50 },
            { "intro_building_upgrades",      1000 },  // enough for 1000e L2 speed upgrade
            { "upgrade_atom_generator_speed", 1000 },
            { "upgrade_context",              200 },
            { "post_tutorial_intro",          200 },
            { "buy_atomic_assembly",          500 },   // enough for 500e Atomic Assembly
            { "atomic_assembly_unlocked",      50 },
            { "buy_heavy_elements",          3000 },   // enough for 3000e Heavy Elements
            { "heavy_elements_unlocked",       50 },
            { "buy_isotope_engineering",     1500 },   // enough for 1500e Isotope Engineering
            { "isotopes_unlocked",             50 },
            { "buy_radioactive_isotopes",    5000 },   // enough for 5000e Radioactive Isotopes
            { "radioactive_isotopes_unlocked", 50 },
        };

        /// <summary>
        /// Returns the entropy to grant when skipping to stepIndex.
        /// Walks backward from stepIndex to find the nearest entry in the table.
        /// Falls back to 250 (default start) if no entry precedes the target step.
        /// </summary>
        public static long GetEntropy(TutorialFlowSO flow, int stepIndex)
        {
            long result = 250;
            int count = flow.steps?.Length ?? 0;
            for (int i = 0; i <= stepIndex && i < count; i++)
            {
                if (s_EntropyTable.TryGetValue(flow.steps[i].id, out long e))
                    result = e;
            }
            return result;
        }

        /// <summary>
        /// Returns the cumulative entropy spent reaching stepIndex, derived from
        /// s_EntropyTable by summing every downward step (each drop = a purchase).
        /// </summary>
        public static long GetTotalEntropySpent(TutorialFlowSO flow, int stepIndex)
        {
            long current     = 0;
            long totalSpent  = 0;
            bool initialized = false;
            int  count       = flow.steps?.Length ?? 0;

            for (int i = 0; i <= stepIndex && i < count; i++)
            {
                if (!s_EntropyTable.TryGetValue(flow.steps[i].id, out long e)) continue;
                if (!initialized) { current = e; initialized = true; continue; }
                if (e < current) totalSpent += current - e;
                current = e;
            }
            return totalSpent;
        }

        // ── Net worth lookup ──────────────────────────────────────────────────
        // Expected inventory net worth at each phase entry point.
        // Set as PlayerProgressData.BaseNetWorth so NetWorthSystem adds it to
        // the live inventory sum — gives meaningful show-progress output after a skip.
        // Calibrated against the economy docs; update after captured playthroughs.

        static readonly Dictionary<string, float> s_NetWorthTable = new()
        {
            { "intro_dialogue",                   0f },
            { "place_conveyor",                 100f },   // automation just started
            { "automation_started",             200f },
            { "intro_sfc",                      300f },
            { "place_sfc",                      500f },
            { "place_generator",                700f },
            { "link_generator",               1_000f },
            { "place_quark_harvester",        1_500f },
            { "route_quarks_to_sfc",          2_000f },
            { "configure_sfc_recipes",        2_500f },
            { "route_nucleons_to_demon",      3_000f },
            { "nucleon_loop_complete",        4_000f },
            { "intro_atom_gen",               5_000f },
            { "place_atom_generator",         6_000f },
            { "connect_atom_gen_inputs",      7_000f },
            { "route_hydrogen_to_demon",      8_000f },
            { "hydrogen_loop_complete",      10_000f },
            { "intro_building_upgrades",     12_000f },
            { "upgrade_atom_generator_speed",15_000f },
            { "upgrade_context",             18_000f },
            { "post_tutorial_intro",         20_000f },
            { "buy_atomic_assembly",         22_000f },
            { "atomic_assembly_unlocked",    24_000f },
            { "craft_first_helium",          26_000f },
            { "first_element_context",       28_000f },
            { "buy_heavy_elements",          30_000f },
            { "heavy_elements_unlocked",     32_000f },
            { "craft_first_uranium",         35_000f },
            { "uranium_crafted",             38_000f },
            { "buy_isotope_engineering",     40_000f },
            { "isotopes_unlocked",           42_000f },
            { "place_isotopic_manipulator",  44_000f },
            { "craft_first_isotope",         45_000f },
            { "isotope_crafted",             46_000f },
            { "buy_radioactive_isotopes",    47_000f },
            { "radioactive_isotopes_unlocked",48_000f },
            { "place_radioactive_containment",49_000f },
            { "craft_uranium_235",           50_000f },
            { "uranium_235_crafted",         51_000f },
            { "collect_alpha_particle",      52_000f },
            { "particles_loop_complete",     55_000f },
        };

        /// <summary>
        /// Returns the BaseNetWorth floor to set when skipping to stepIndex.
        /// Walks backward from stepIndex to find the nearest entry, falls back to 0.
        /// </summary>
        public static float GetNetWorth(TutorialFlowSO flow, int stepIndex)
        {
            float result = 0f;
            int count = flow.steps?.Length ?? 0;
            for (int i = 0; i <= stepIndex && i < count; i++)
            {
                if (s_NetWorthTable.TryGetValue(flow.steps[i].id, out float nw))
                    result = nw;
            }
            return result;
        }

        // ── Grid presets ──────────────────────────────────────────────────────
        // Building and conveyor presets for each grid checkpoint.
        //
        // HOW TO FILL THESE IN:
        //   1. Play through the tutorial to the target step normally.
        //   2. Type "save" in the dev console to flush state to disk.
        //   3. Open the local save file (find path via SaveManager.SavePath or PlayerPrefs key).
        //   4. Copy the "grid.buildings" and "grid.conveyors" arrays into the constants below.
        //   5. Remove the "null" assignment and replace with the array literal.
        //
        // Building ID reference (confirmed from save data / game_data.json): 1=Harvester 2=SFC 3=BasicGenerator 7=MaxwellsDemon
        // outputDirection: -1=none 0=North 1=East 2=South 3=West
        // outputDirection: -1=none 0=North 1=East 2=South 3=West

        // ── Field presets ─────────────────────────────────────────────────────
        // Field positions must match the harvester positions in the building presets above.
        // fieldId values: "quark_field", "electron_field"

        static readonly FieldSaveData[] k_PlaceConveyor_Fields = new[]
        {
            new FieldSaveData { fieldId = "electron_field", position = new[] {  6, 10 } }, // electron harvester
            new FieldSaveData { fieldId = "quark_field",    position = new[] {  5,  8 } }, // quark harvester
            new FieldSaveData { fieldId = "quark_field",    position = new[] {  4,  8 } }, // quark harvester
        };

        static readonly Dictionary<string, FieldSaveData[]> s_FieldTable = new()
        {
            { "place_conveyor",     k_PlaceConveyor_Fields },
            { "automation_started", k_PlaceConveyor_Fields },
            { "intro_sfc",          k_PlaceConveyor_Fields },
        };

        public static FieldSaveData[] GetFields(string stepId)
        {
            return s_FieldTable.TryGetValue(stepId, out var fields) ? fields : null;
        }

        // ── Step 31: place_conveyor
        // State: Harvester placed on a field, Maxwell's Demon present; player must draw conveyor.
        // Maxwell's Demon (buildingId=7) is intentionally excluded — it is an entropy sink that
        // ClearGrid preserves in place. Including it here would cause LoadGrid to attempt a duplicate.
        static readonly BuildingSaveData[] k_PlaceConveyor_Buildings = new[]
        {
            new BuildingSaveData { buildingId = 1, recipeId =  3, position = new[] {  6, 10 }, level = 1, rotation = 0, flipped = false, outputDirection = -1 }, // Harvester (electron)
            new BuildingSaveData { buildingId = 1, recipeId =  1, position = new[] {  5,  8 }, level = 1, rotation = 0, flipped = false, outputDirection = -1 }, // Harvester (quark)
            new BuildingSaveData { buildingId = 1, recipeId =  2, position = new[] {  4,  8 }, level = 1, rotation = 0, flipped = false, outputDirection = -1 }, // Harvester (quark)
        };
        static readonly ConveyorSaveData[] k_PlaceConveyor_Conveyors = new ConveyorSaveData[0];

        // ── Step 34: place_sfc
        // State: Harvester + Demon connected via conveyor; player places SFC.
        static readonly BuildingSaveData[] k_PlaceSfc_Buildings        = null; // TODO
        static readonly ConveyorSaveData[] k_PlaceSfc_Conveyors         = null;

        // ── Step 41: nucleon_loop_complete
        // State: Harvester(e) + Harvester(q) + SFC + Generator + Maxwell's Demon + full nucleon pipeline.
        static readonly BuildingSaveData[] k_NucleonLoop_Buildings     = null; // TODO
        static readonly ConveyorSaveData[] k_NucleonLoop_Conveyors      = null;

        // ── Step 43: place_atom_generator
        // State: Full nucleon pipeline running; player places Atom Generator.
        static readonly BuildingSaveData[] k_PlaceAtomGen_Buildings    = null; // TODO
        static readonly ConveyorSaveData[] k_PlaceAtomGen_Conveyors     = null;

        // ── Step 46: hydrogen_loop_complete
        // State: Nucleon pipeline + Atom Generator + full hydrogen loop to Maxwell's Demon.
        static readonly BuildingSaveData[] k_HydrogenLoop_Buildings    = null; // TODO
        static readonly ConveyorSaveData[] k_HydrogenLoop_Conveyors     = null;

        // ── Step 48: upgrade_atom_generator_speed
        // State: Full hydrogen loop; Atom Generator at level 1; player buys L2 speed upgrade.
        static readonly BuildingSaveData[] k_UpgradeSpeed_Buildings    = null; // TODO
        static readonly ConveyorSaveData[] k_UpgradeSpeed_Conveyors     = null;

        // ── Step 61: place_isotopic_manipulator
        // State: Full hydrogen loop + Atom Generator L2; player places Isotopic Manipulator.
        static readonly BuildingSaveData[] k_PlaceManip_Buildings      = null; // TODO
        static readonly ConveyorSaveData[] k_PlaceManip_Conveyors       = null;

        // ── Step 66: place_radioactive_containment
        // State: Full loop + Manipulator; player places Radioactive Containment.
        static readonly BuildingSaveData[] k_PlaceContain_Buildings    = null; // TODO
        static readonly ConveyorSaveData[] k_PlaceContain_Conveyors     = null;

        // Lookup table: stepId → (buildings, conveyors) pair
        // Steps not listed here will use the nearest preceding defined checkpoint.
        static readonly Dictionary<string, (BuildingSaveData[] buildings, ConveyorSaveData[] conveyors)> s_GridTable = new()
        {
            { "place_conveyor",               (k_PlaceConveyor_Buildings,  k_PlaceConveyor_Conveyors) },
            { "automation_started",           (k_PlaceConveyor_Buildings,  k_PlaceConveyor_Conveyors) },
            { "intro_sfc",                    (k_PlaceConveyor_Buildings,  k_PlaceConveyor_Conveyors) },
            { "place_sfc",                    (k_PlaceSfc_Buildings,       k_PlaceSfc_Conveyors) },
            { "place_generator",              (k_PlaceSfc_Buildings,       k_PlaceSfc_Conveyors) },
            { "link_generator",               (k_PlaceSfc_Buildings,       k_PlaceSfc_Conveyors) },
            { "place_quark_harvester",        (k_PlaceSfc_Buildings,       k_PlaceSfc_Conveyors) },
            { "route_quarks_to_sfc",          (k_PlaceSfc_Buildings,       k_PlaceSfc_Conveyors) },
            { "configure_sfc_recipes",        (k_PlaceSfc_Buildings,       k_PlaceSfc_Conveyors) },
            { "route_nucleons_to_demon",      (k_PlaceSfc_Buildings,       k_PlaceSfc_Conveyors) },
            { "nucleon_loop_complete",        (k_NucleonLoop_Buildings,    k_NucleonLoop_Conveyors) },
            { "intro_atom_gen",               (k_NucleonLoop_Buildings,    k_NucleonLoop_Conveyors) },
            { "place_atom_generator",         (k_PlaceAtomGen_Buildings,   k_PlaceAtomGen_Conveyors) },
            { "connect_atom_gen_inputs",      (k_PlaceAtomGen_Buildings,   k_PlaceAtomGen_Conveyors) },
            { "route_hydrogen_to_demon",      (k_PlaceAtomGen_Buildings,   k_PlaceAtomGen_Conveyors) },
            { "hydrogen_loop_complete",       (k_HydrogenLoop_Buildings,   k_HydrogenLoop_Conveyors) },
            { "intro_building_upgrades",      (k_HydrogenLoop_Buildings,   k_HydrogenLoop_Conveyors) },
            { "upgrade_atom_generator_speed", (k_UpgradeSpeed_Buildings,   k_UpgradeSpeed_Conveyors) },
            { "upgrade_context",              (k_UpgradeSpeed_Buildings,   k_UpgradeSpeed_Conveyors) },
            { "post_tutorial_intro",          (k_UpgradeSpeed_Buildings,   k_UpgradeSpeed_Conveyors) },
            { "buy_atomic_assembly",          (k_UpgradeSpeed_Buildings,   k_UpgradeSpeed_Conveyors) },
            { "atomic_assembly_unlocked",     (k_UpgradeSpeed_Buildings,   k_UpgradeSpeed_Conveyors) },
            { "craft_first_helium",           (k_UpgradeSpeed_Buildings,   k_UpgradeSpeed_Conveyors) },
            { "first_element_context",        (k_UpgradeSpeed_Buildings,   k_UpgradeSpeed_Conveyors) },
            { "buy_heavy_elements",           (k_UpgradeSpeed_Buildings,   k_UpgradeSpeed_Conveyors) },
            { "heavy_elements_unlocked",      (k_UpgradeSpeed_Buildings,   k_UpgradeSpeed_Conveyors) },
            { "craft_first_uranium",          (k_UpgradeSpeed_Buildings,   k_UpgradeSpeed_Conveyors) },
            { "uranium_crafted",              (k_UpgradeSpeed_Buildings,   k_UpgradeSpeed_Conveyors) },
            { "buy_isotope_engineering",      (k_UpgradeSpeed_Buildings,   k_UpgradeSpeed_Conveyors) },
            { "isotopes_unlocked",            (k_UpgradeSpeed_Buildings,   k_UpgradeSpeed_Conveyors) },
            { "place_isotopic_manipulator",   (k_PlaceManip_Buildings,     k_PlaceManip_Conveyors) },
            { "craft_first_isotope",          (k_PlaceManip_Buildings,     k_PlaceManip_Conveyors) },
            { "isotope_crafted",              (k_PlaceManip_Buildings,     k_PlaceManip_Conveyors) },
            { "buy_radioactive_isotopes",     (k_PlaceManip_Buildings,     k_PlaceManip_Conveyors) },
            { "radioactive_isotopes_unlocked",(k_PlaceContain_Buildings,   k_PlaceContain_Conveyors) },
            { "place_radioactive_containment",(k_PlaceContain_Buildings,   k_PlaceContain_Conveyors) },
            { "craft_uranium_235",            (k_PlaceContain_Buildings,   k_PlaceContain_Conveyors) },
            { "uranium_235_crafted",          (k_PlaceContain_Buildings,   k_PlaceContain_Conveyors) },
            { "collect_alpha_particle",       (k_PlaceContain_Buildings,   k_PlaceContain_Conveyors) },
            { "particles_loop_complete",      (k_PlaceContain_Buildings,   k_PlaceContain_Conveyors) },
        };

        /// <summary>
        /// Returns the preset buildings for this step, or null if no grid preset is defined.
        /// </summary>
        public static BuildingSaveData[] GetBuildings(string stepId)
        {
            return s_GridTable.TryGetValue(stepId, out var pair) ? pair.buildings : null;
        }

        /// <summary>
        /// Returns the preset conveyors for this step, or null if no grid preset is defined.
        /// </summary>
        public static ConveyorSaveData[] GetConveyors(string stepId)
        {
            return s_GridTable.TryGetValue(stepId, out var pair) ? pair.conveyors : null;
        }
    }
}
#endif
