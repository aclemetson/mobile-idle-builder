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
        public void AllItems_TotalCount_Is51()
        {
            Assert.AreEqual(51, _items.Length,
                $"Expected 51 items in Resources/Items/, found {_items.Length}. " +
                "Did a game_data.json import add or remove an item?");
        }

        // ── Tier 1 — Subatomic ───────────────────────────────────────────────

        [Test] public void T1_UpQuark_SellValue()     => AssertSell("up_quark",   1f);
        [Test] public void T1_DownQuark_SellValue()   => AssertSell("down_quark", 1f);
        [Test] public void T1_Electron_SellValue()    => AssertSell("electron",   1f);
        [Test] public void T1_Proton_SellValue()      => AssertSell("proton",     3f);
        [Test] public void T1_Neutron_SellValue()     => AssertSell("neutron",    3f);

        // ── Tier 2 — Elements (×2 ladder anchored at H=5) ───────────────────

        [Test] public void T2_Hydrogen_SellValue()    => AssertSell("hydrogen",   5f);
        [Test] public void T2_Helium4_SellValue()     => AssertSell("helium_4",   10f);
        [Test] public void T2_Lithium_SellValue()     => AssertSell("lithium",    20f);
        [Test] public void T2_Beryllium_SellValue()   => AssertSell("beryllium",  40f);
        [Test] public void T2_Boron_SellValue()       => AssertSell("boron",      80f);
        [Test] public void T2_Carbon_SellValue()      => AssertSell("carbon",     160f);
        [Test] public void T2_Nitrogen_SellValue()    => AssertSell("nitrogen",   320f);
        [Test] public void T2_Oxygen_SellValue()      => AssertSell("oxygen",     640f);
        [Test] public void T2_Silicon_SellValue()     => AssertSell("silicon",    1280f);
        [Test] public void T2_Aluminum_SellValue()    => AssertSell("aluminum",   2560f);
        [Test] public void T2_Iron_SellValue()        => AssertSell("iron",       5120f);
        [Test] public void T2_Nickel_SellValue()      => AssertSell("nickel",     10240f);
        [Test] public void T2_Copper_SellValue()      => AssertSell("copper",     20480f);
        [Test] public void T2_Zinc_SellValue()        => AssertSell("zinc",       40960f);
        [Test] public void T2_Silver_SellValue()      => AssertSell("silver",     81920f);
        [Test] public void T2_Gold_SellValue()        => AssertSell("gold",       163840f);
        [Test] public void T2_Platinum_SellValue()    => AssertSell("platinum",   327680f);
        [Test] public void T2_Tungsten_SellValue()    => AssertSell("tungsten",   655360f);
        [Test] public void T2_Uranium_SellValue()     => AssertSell("uranium",    1310720f);
        [Test] public void T2_Plutonium_SellValue()   => AssertSell("plutonium",  2621440f);

        // ── Tier 2 — Isotopes ────────────────────────────────────────────────

        [Test] public void T2_Deuterium_SellValue()   => AssertSell("deuterium",    8f);
        [Test] public void T2_Tritium_SellValue()     => AssertSell("tritium",      12f);
        [Test] public void T2_Carbon14_SellValue()    => AssertSell("carbon_14",    240f);
        [Test] public void T2_Uranium235_SellValue()  => AssertSell("uranium_235",  2621440f);

        // ── Tier 2 — Decay particles ─────────────────────────────────────────

        [Test] public void T2_AlphaParticle_SellValue() => AssertSell("alpha_particle", 50f);
        [Test] public void T2_BetaParticle_SellValue()  => AssertSell("beta_particle",  30f);

        // ── Tier 3 — Molecular ───────────────────────────────────────────────

        [Test] public void T3_LiquidHydrogen_SellValue()       => AssertSell("liquid_hydrogen",  300f);
        [Test] public void T3_Water_SellValue()                 => AssertSell("water",            6000f);
        [Test] public void T3_Methane_SellValue()               => AssertSell("methane",          5000f);
        [Test] public void T3_Ammonia_SellValue()               => AssertSell("ammonia",          10000f);
        [Test] public void T3_Silica_SellValue()                => AssertSell("silica",           50000f);
        [Test] public void T3_IronOxide_SellValue()             => AssertSell("iron_oxide",       200000f);
        [Test] public void T3_UraniumHexafluoride_SellValue()   => AssertSell("uranium_hexafluoride", 8000000f);

        // ── Tier 4 — Materials ───────────────────────────────────────────────

        [Test] public void T4_Steel_SellValue()             => AssertSell("steel",              1500000f);
        [Test] public void T4_CarbonFiber_SellValue()       => AssertSell("carbon_fiber",       6000000f);
        [Test] public void T4_TitaniumAlloy_SellValue()     => AssertSell("titanium_alloy",     25000000f);
        [Test] public void T4_SemiconductorWafer_SellValue() => AssertSell("semiconductor_wafer", 100000000f);
        [Test] public void T4_Aerogel_SellValue()           => AssertSell("aerogel",            400000000f);
        [Test] public void T4_Superconductor_SellValue()    => AssertSell("superconductor",     1600000000f);
        [Test] public void T4_Metamaterial_SellValue()      => AssertSell("metamaterial",       6400000000f);

        // ── Tier 5 — Components ──────────────────────────────────────────────

        [Test] public void T5_QuantumProcessor_SellValue()        => AssertSell("quantum_processor",       5e10f);
        [Test] public void T5_PlasmaContainmentRing_SellValue()   => AssertSell("plasma_containment_ring", 2e11f);
        [Test] public void T5_AntimatterCell_SellValue()          => AssertSell("antimatter_cell",         8e11f);
        [Test] public void T5_DysonNode_SellValue()               => AssertSell("dyson_node",              5e12f);
        [Test] public void T5_OrbitalFrame_SellValue()            => AssertSell("orbital_frame",           2e13f);
        [Test] public void T5_GravitonLens_SellValue()            => AssertSell("graviton_lens",           1e14f);

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
