using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// #488: the Recipes menu's item-type tabs, the method names on cards and in the drop-up, and the shared tab
    /// helpers (hidden empty tabs, tutorial guidance to a hidden tab).
    /// </summary>
    public class RecipeMenuTests
    {
        static RecipeJson R(string id, string label, params string[] craftedIn) => new RecipeJson
        {
            id = id,
            method_label = label,
            output = new RecipeOutputJson { id = "helium_4", quantity = 1 },
            inputs = new List<RecipeIngredientJson> { new RecipeIngredientJson { id = "x", quantity = 1 } },
            crafted_in = craftedIn.ToList(),
        };

        static string Name(string id) => id switch
        {
            "fusion_reactor"   => "Fusion Reactor",
            "atomic_assembler" => "Atom Generator",
            _                  => null,
        };

        [TestCase(ItemCategory.Nucleon, "particles")]
        [TestCase(ItemCategory.Particle, "particles")]
        [TestCase(ItemCategory.Element, "elements")]
        [TestCase(ItemCategory.Isotope, "isotopes")]
        [TestCase(ItemCategory.Molecule, "molecules")]
        [TestCase(ItemCategory.OrganicCompound, "molecules")]
        [TestCase(ItemCategory.Alloy, "materials")]
        [TestCase(ItemCategory.Component, "materials")]
        [TestCase(ItemCategory.Megastructure, "materials")]
        [TestCase(ItemCategory.Biomolecule, "life")]
        [TestCase(ItemCategory.Organism, "life")]
        public void Tabs_GroupItemsByType(ItemCategory category, string tab) =>
            Assert.AreEqual(tab, RecipeMenu.TabOf(category));

        [Test]
        public void EveryCategory_LandsOnAKnownTab()
        {
            foreach (ItemCategory c in System.Enum.GetValues(typeof(ItemCategory)))
                CollectionAssert.Contains(RecipeMenu.Tabs, RecipeMenu.TabOf(c), c.ToString());
            CollectionAssert.Contains(RecipeMenu.Tabs, RecipeMenu.TabOf(null));
        }

        [Test]
        public void MethodTitle_SaysWhereAndHow()
        {
            Assert.AreEqual("Made in a Fusion Reactor · p-p chain",
                RecipeMenu.MethodTitle(R("fuse_helium", "p-p chain", "fusion_reactor"), Name));
            Assert.AreEqual("Made in an Atom Generator", RecipeMenu.MethodTitle(R("helium_4", null, "atomic_assembler"), Name));
            Assert.AreEqual("Craft from the menu", RecipeMenu.MethodTitle(R("proton", null, "inventory_menu"), Name));
        }

        [Test]
        public void ShortMethodTitle_IsTheLabel_ElseTheBuilding()
        {
            Assert.AreEqual("D-D fusion", RecipeMenu.ShortMethodTitle(R("fuse_d", "D-D fusion", "fusion_reactor"), Name));
            Assert.AreEqual("Atom Generator / Fusion Reactor",
                RecipeMenu.ShortMethodTitle(R("he", null, "atomic_assembler", "fusion_reactor"), Name));
            Assert.AreEqual("From the menu", RecipeMenu.ShortMethodTitle(R("p", null, "inventory_menu"), Name));
        }

        [Test]
        public void WaysToggle_ReadsCountClosed_CloseOpen()
        {
            Assert.AreEqual("3 ways to make", RecipeMenu.WaysText(3, false));
            Assert.AreEqual("Close", RecipeMenu.WaysText(3, true));
        }

        [Test]
        public void EmptyTabs_Hide_AndTheTutorialGuidesToAHiddenRecipeTab()
        {
            var root = new VisualElement();
            var pages = new Dictionary<string, VisualElement>();
            var bar = MenuTabs.MakeTabBar(RecipeMenu.Prefix, RecipeMenu.Tabs, RecipeMenu.TabLabel, null);
            root.Add(bar);
            foreach (var tab in RecipeMenu.Tabs)
            {
                pages[tab] = MenuTabs.MakePage(RecipeMenu.Prefix, tab, null);
                root.Add(pages[tab]);
            }
            var target = new Button { name = "btn-target" };
            pages["isotopes"].Add(target);
            pages["elements"].Add(new Button());
            MenuTabs.ShowTab(RecipeMenu.Prefix, bar, pages, "elements");

            Assert.IsTrue(bar.Q(MenuTabs.TabButtonName(RecipeMenu.Prefix, "life")).ClassListContains("hidden"),
                "a tab with no cards hides");
            Assert.IsFalse(bar.Q(MenuTabs.TabButtonName(RecipeMenu.Prefix, "isotopes")).ClassListContains("hidden"));
            Assert.AreEqual(MenuTabs.TabButtonName(RecipeMenu.Prefix, "isotopes"),
                TutorialPulseResolver.Resolve(root, "btn-target")?.name);
            Assert.AreEqual("elements", MenuTabs.PickTab("life", new HashSet<string> { "elements", "isotopes" }, RecipeMenu.Tabs));
        }
    }
}
