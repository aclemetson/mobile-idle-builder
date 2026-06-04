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
        // Building ID reference: 1=Harvester 2=SFC 3=Generator 4=MaxwellsDemon 5=AtomicAssembler 6=IsoManipulator 7=RadioactiveContainment
        // outputDirection: -1=none 0=North 1=East 2=South 3=West

        // ── Step 31: place_conveyor
        // State: Harvester placed on a field, Maxwell's Demon present; player must draw conveyor.
        static readonly BuildingSaveData[] k_PlaceConveyor_Buildings  = null; // TODO: capture from playthrough
        static readonly ConveyorSaveData[] k_PlaceConveyor_Conveyors   = null;

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
