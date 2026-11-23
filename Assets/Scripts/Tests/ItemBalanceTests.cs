using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Edit Mode tests that verify each item's baseSellValue matches the design-doc
    /// balance table. These act as a canary for game_data.json import regressions:
    /// if a value is edited in the JSON and re-imported, the affected test fails.
    ///
    /// Tier coverage: T1 (Subatomic), T2 (Atomic — elements, isotopes, particles),
    /// T3 (Molecular), T4 (Materials), T5 (Components).
    /// Base values only — prestige/research multipliers are tested separately.
    /// </summary>
    [TestFixture]
    public class ItemBalanceTests
    {
        private ItemSO[] _items;

        [OneTimeSetUp]
        public void LoadItems()
        {
            _items = Resources.LoadAll<ItemSO>("Items");
            Assert.IsNotNull(_items,           "Resources.LoadAll<ItemSO>(\"Items\") returned null");
            Assert.IsNotEmpty(_items,          "No ItemSO assets found in Resources/Items/");
        }

        // ── Item count ───────────────────────────────────────────────────────

        [Test]
        public void AllItems_TotalCount_Is187()
        {
            // 149 physics + 4 OrganicCompound (P2) + 3 C1 (P4a) + 8 C2-C3 (P4b) + 4 B1 Biomolecules
            // + 10 B2-B4 (4 CellPart B2 + 3 B3 + 3 Organism B4) + 2 fusion particles (neutrino, gamma)
            // + 6 decay-chain isotopes (#322) - Tritium and Carbon-14 (retired 2026-11-19).
            Assert.AreEqual(187, _items.Length,
                $"Expected 187 items in Resources/Items/, found {_items.Length}. " +
                "Did a game_data.json import add or remove an item?");
        }

        // ── Tier 1 — Subatomic ───────────────────────────────────────────────

        [Test] public void T1_UpQuark_SellValue()     => AssertSell("up_quark",   1f);
        [Test] public void T1_DownQuark_SellValue()   => AssertSell("down_quark", 1f);
        [Test] public void T1_Electron_SellValue()    => AssertSell("electron",   1f);
        [Test] public void T1_Proton_SellValue()      => AssertSell("proton",     3f);
        [Test] public void T1_Neutron_SellValue()     => AssertSell("neutron",    3f);

        // ── Tier 2 — Elements (convergent value model: fusion doubles up to the iron
        //    peak Z26 = 167,772,160; fission fragments (Z36-80, ~200-313) and the decay chain (U 64 -> Pb) follow
        //    the #322 rescale; everything made by neutron capture is worth more than its seed (tier value:
        //    slow x1.12, fast x1.6 per step); Np/Pu sit just below uranium (Pu is harvested);
        //    post-iron synthesis climbs from Americium (Z95) to Oganesson = the global max).
        //    See docs/agents/elements-and-isotopes.md. Values = nucYield(Z). ──────────────

        [Test] public void T2_Hydrogen_SellValue()    => AssertSell("hydrogen",   5f);
        [Test] public void T2_Helium4_SellValue()     => AssertSell("helium_4",   15f);   // #270: pays more per proton than Hydrogen
        [Test] public void T2_Lithium_SellValue()     => AssertSell("lithium",    79f);
        [Test] public void T2_Beryllium_SellValue()   => AssertSell("beryllium",  167f);
        [Test] public void T2_Boron_SellValue()       => AssertSell("boron",      276f);
        [Test] public void T2_Carbon_SellValue()      => AssertSell("carbon",     160f);
        [Test] public void T2_Nitrogen_SellValue()    => AssertSell("nitrogen",   313f);
        [Test] public void T2_Oxygen_SellValue()      => AssertSell("oxygen",     351f);
        [Test] public void T2_Silicon_SellValue()     => AssertSell("silicon",    1400f);
        [Test] public void T2_Aluminum_SellValue()    => AssertSell("aluminum",   1280f);
        [Test] public void T2_Iron_SellValue()        => AssertSell("iron",       333000f);   // natural peak
        [Test] public void T2_Cobalt_SellValue()      => AssertSell("cobalt",     298000f);   // first capture step up from iron
        [Test] public void T2_Nickel_SellValue()      => AssertSell("nickel",     286000f);
        [Test] public void T2_Copper_SellValue()      => AssertSell("copper",     275000f);
        [Test] public void T2_Zinc_SellValue()        => AssertSell("zinc",       264000f);
        [Test] public void T2_Technetium_SellValue()  => AssertSell("technetium", 167000f);
        [Test] public void T2_Silver_SellValue()      => AssertSell("silver",     143000f);
        [Test] public void T2_Tungsten_SellValue()    => AssertSell("tungsten",   28700f);
        [Test] public void T2_Platinum_SellValue()    => AssertSell("platinum",   15400f);
        [Test] public void T2_Gold_SellValue()        => AssertSell("gold",       12200f);
        [Test] public void T2_Uranium_SellValue()     => AssertSell("uranium",    3170f);          // cheap fission floor
        [Test] public void T2_Plutonium_SellValue()   => AssertSell("plutonium",  224000f);          // harvested on the Actinide Vein: cheap
        [Test] public void T2_Americium_SellValue()   => AssertSell("americium",  134000f);   // first post-iron synthetic
        [Test] public void T2_Oganesson_SellValue()   => AssertSell("oganesson",  228000f); // global max

        // ── Tier 2 — Isotopes (base = round(nucYield(Z) × isotopeSellMultiplier)) ──────

        // Deuterium sits below half a Helium (0.8x) so fusing two of them is worth more than selling them.
        [Test] public void T2_Deuterium_SellValue()   => AssertSell("deuterium",    4f);
        [Test] public void T2_Uranium235_SellValue()  => AssertSell("uranium_235",  4710f);

        // ── Tier 2 — Decay particles ─────────────────────────────────────────

        [Test] public void T2_AlphaParticle_SellValue() => AssertSell("alpha_particle", 10f);   // a bare Helium nucleus
        [Test] public void T2_BetaParticle_SellValue()  => AssertSell("beta_particle",  2f);    // an electron

        // ── Tier 2 — Fusion particles (escape the reactor; paid as entropy per craft) ─────

        [Test] public void T2_Neutrino_SellValue()      => AssertSell("neutrino",     3f);
        [Test] public void T2_GammaPhoton_SellValue()   => AssertSell("gamma_photon", 5f);

        // ── Tier 3 — Molecular ───────────────────────────────────────────────

        [Test] public void T3_Water_SellValue()                 => AssertSell("water",            645f);
        [Test] public void T3_Methane_SellValue()               => AssertSell("methane",          3000f);
        [Test] public void T3_Ammonia_SellValue()               => AssertSell("ammonia",          1710f);
        [Test] public void T3_Silica_SellValue()                => AssertSell("silica",           4010f);
        [Test] public void T3_IronOxide_SellValue()             => AssertSell("iron_oxide",       671000f);
        [Test] public void T3_UraniumHexafluoride_SellValue()   => AssertSell("uranium_hexafluoride", 29100f);

        // ── Tier 3 — Chemistry compounds (C1 P4a, C2-C3 P4b). First-pass values, re-tier in Phase 6. ──

        [Test] public void T3_CarbonDioxide_SellValue()   => AssertSell("carbon_dioxide",    1670f);
        [Test] public void T3_TableSalt_SellValue()       => AssertSell("table_salt",        6300f);
        [Test] public void T3_SulfuricAcid_SellValue()    => AssertSell("sulfuric_acid",     6360f);
        [Test] public void T3_Catalyst_SellValue()        => AssertSell("catalyst",          624000f);
        [Test] public void T3_ChlorineGas_SellValue()     => AssertSell("chlorine_gas",      7000f);
        [Test] public void T3_SodiumHydroxide_SellValue() => AssertSell("sodium_hydroxide",  2280f);
        [Test] public void T3_NitricAcid_SellValue()      => AssertSell("nitric_acid",       2030f);
        [Test] public void T3_Ethane_SellValue()          => AssertSell("ethane",            4110f);
        [Test] public void T3_Octane_SellValue()          => AssertSell("octane",            21000f);
        [Test] public void T3_Ethanol_SellValue()         => AssertSell("ethanol",           5920f);
        [Test] public void T3_PolymerPrecursor_SellValue() => AssertSell("polymer_precursor", 651000f);

        // ── Tier 3 — Biology B1 biomolecules. First-pass values, re-tier in Phase 6. ──

        [Test] public void T3_Protein_SellValue()       => AssertSell("protein",       32300f);
        [Test] public void T3_Carbohydrate_SellValue()  => AssertSell("carbohydrate",  56900f);
        [Test] public void T3_LipidMembrane_SellValue() => AssertSell("lipid_membrane", 219000f);
        [Test] public void T3_NucleicAcid_SellValue()   => AssertSell("nucleic_acid",  114000f);

        // ── Tier 3 — Biology B2-B4 (cells -> organs -> ecosystems). First-pass, re-tier in Phase 6. ──

        [Test] public void T3_Ribosome_SellValue()        => AssertSell("ribosome",         190000f);
        [Test] public void T3_Mitochondria_SellValue()    => AssertSell("mitochondria",     295000f);
        [Test] public void T3_CellMembrane_SellValue()    => AssertSell("cell_membrane",    480000f);
        [Test] public void T3_ProkaryoticCell_SellValue() => AssertSell("prokaryotic_cell", 982000f);
        [Test] public void T3_EukaryoticCell_SellValue()  => AssertSell("eukaryotic_cell",  1300000f);
        [Test] public void T3_Tissue_SellValue()          => AssertSell("tissue",           5230000f);
        [Test] public void T3_Organ_SellValue()           => AssertSell("organ",            21000000f);
        [Test] public void T3_Organism_SellValue()        => AssertSell("organism",         84100000f);
        [Test] public void T3_Population_SellValue()      => AssertSell("population",        421000000f);
        [Test] public void T3_Ecosystem_SellValue()       => AssertSell("ecosystem",        2110000000f);

        // ── Tier 4 — Materials ───────────────────────────────────────────────

        [Test] public void T4_Steel_SellValue()             => AssertSell("steel",              3410000f);
        [Test] public void T4_CarbonFiber_SellValue()       => AssertSell("carbon_fiber",       78300f);
        [Test] public void T4_TitaniumAlloy_SellValue()     => AssertSell("titanium_alloy",     2770000f);
        [Test] public void T4_SemiconductorWafer_SellValue() => AssertSell("semiconductor_wafer", 118000f);
        [Test] public void T4_Aerogel_SellValue()           => AssertSell("aerogel",            129000f);
        [Test] public void T4_Superconductor_SellValue()    => AssertSell("superconductor",     475000f);
        [Test] public void T4_Metamaterial_SellValue()      => AssertSell("metamaterial",       1350000f);

        // ── Tier 5 — Components ──────────────────────────────────────────────

        [Test] public void T5_QuantumProcessor_SellValue()        => AssertSell("quantum_processor",       2410000f);
        [Test] public void T5_PlasmaContainmentRing_SellValue()   => AssertSell("plasma_containment_ring", 12400000f);
        [Test] public void T5_AntimatterCell_SellValue()          => AssertSell("antimatter_cell",         6150000f);
        [Test] public void T5_DysonNode_SellValue()               => AssertSell("dyson_node",              32900000f);
        [Test] public void T5_OrbitalFrame_SellValue()            => AssertSell("orbital_frame",           78500000f);
        [Test] public void T5_GravitonLens_SellValue()            => AssertSell("graviton_lens",           175000000f);

        // ── Helper ───────────────────────────────────────────────────────────

        private void AssertSell(string id, float expected)
        {
            ItemSO item = null;
            foreach (var so in _items)
            {
                if (so.id == id) { item = so; break; }
            }

            Assert.IsNotNull(item, $"ItemSO with id '{id}' not found in Resources/Items/");

            // Use relative tolerance of 0.01% to handle float imprecision on large values
            float tolerance = Mathf.Max(0.5f, Mathf.Abs(expected) * 0.0001f);
            Assert.AreEqual(expected, item.baseSellValue, tolerance,
                $"Item '{id}' baseSellValue mismatch — expected {expected}, got {item.baseSellValue}. " +
                "Re-run 'MobileIdleBuilder > Import Game Data' if game_data.json was changed.");
        }
    }
}
