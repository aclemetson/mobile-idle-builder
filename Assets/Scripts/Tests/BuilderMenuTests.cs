using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// #487: the Builder's role tabs, card pictures (rendered building, else product icon), the card layout and
    /// the tutorial pulse guiding to a building on a hidden tab.
    /// </summary>
    public class BuilderMenuTests
    {
        static BuildingSO Load(string id)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:BuildingSO"))
            {
                var b = AssetDatabase.LoadAssetAtPath<BuildingSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (b != null && b.id == id) return b;
            }
            Assert.Fail($"building {id} not found");
            return null;
        }

        [TestCase("harvester", "harvest")]
        [TestCase("element_harvester", "harvest")]
        [TestCase("nucleon_harvester", "harvest")]
        [TestCase("spring", "harvest")]
        [TestCase("atomic_assembler", "production")]
        [TestCase("fusion_reactor", "production")]
        [TestCase("molecular_synthesizer", "production")]
        [TestCase("filter", "logistics")]
        [TestCase("basic_generator", "power")]
        [TestCase("power_relay", "power")]
        [TestCase("maxwells_demon", "special")]
        [TestCase("calorimeter", "special")]
        public void Tabs_GroupBuildingsByRole(string id, string tab) =>
            Assert.AreEqual(tab, BuilderMenu.TabOf(Load(id)), id);

        [Test]
        public void EveryBuilding_LandsOnAKnownTab()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:BuildingSO"))
            {
                var b = AssetDatabase.LoadAssetAtPath<BuildingSO>(AssetDatabase.GUIDToAssetPath(guid));
                CollectionAssert.Contains(BuilderMenu.Tabs, BuilderMenu.TabOf(b), b.id);
            }
        }

        [Test]
        public void Picture_RenderedBuilding_ElseItsProduct()
        {
            var assembler = Load("atomic_assembler");
            Assert.IsNotNull(assembler.icon, "rendered icon imported (run Generate Building Icons + Import Game Data)");
            Assert.AreSame(assembler.icon, BuilderMenu.CardSprite(assembler, null));

            var bioreactor = Load("bioreactor");   // shares the Molecular Synthesizer form: no render of its own
            Assert.IsNull(bioreactor.icon);
            var sprite = BuilderMenu.CardSprite(bioreactor, null);
            Assert.IsNotNull(sprite, "falls back to a product icon");
            Assert.IsNull(BuilderMenu.CardSprite(null, null));
        }

        [Test]
        public void PickTab_RemembersAChoiceWithCards_ElseTheFirstWithCards()
        {
            var with = new HashSet<string> { "production", "power" };
            Assert.AreEqual("power", BuilderMenu.PickTab("power", with));
            Assert.AreEqual("production", BuilderMenu.PickTab("special", with), "remembered tab is empty");
            Assert.AreEqual("production", BuilderMenu.PickTab(null, with));
        }

        [Test]
        public void Card_KeepsTheTutorialHooks_AndShowsAGlyphWithoutAPicture()
        {
            var place = new Button { name = TutorialPulseResolver.PlaceButtonName("atomic_assembler") };
            var card = BuilderMenu.MakeCard("Atom Generator", null, GlyphIcon.Kind.Build, new Label("◈ 350"), place, new Button());
            Assert.IsTrue(card.ClassListContains("building-card"), "the tutorial highlights building-card");
            Assert.IsNotNull(card.Q<Button>(TutorialPulseResolver.PlaceButtonName("atomic_assembler")));
            Assert.IsNotNull(card.Q(className: "building-tile__glyph"));
            Assert.IsNotNull(card.Q(className: "building-tile__info"));
        }

        [Test]
        public void TutorialPulse_GuidesToTheTabOfAHiddenCard()
        {
            var root = new VisualElement();
            var pages = new Dictionary<string, VisualElement>();
            VisualElement bar = null;
            bar = BuilderMenu.MakeTabBar(t => BuilderMenu.ShowTab(bar, pages, t));
            root.Add(bar);
            foreach (var tab in BuilderMenu.Tabs)
            {
                var page = new VisualElement { name = BuilderMenu.PageName(tab) };
                page.AddToClassList(BuilderMenu.PageClass);
                pages[tab] = page;
                root.Add(page);
            }
            var place = new Button { name = TutorialPulseResolver.PlaceButtonName("conveyor") };
            pages["logistics"].Add(place);
            pages["production"].Add(new Button());
            BuilderMenu.ShowTab(bar, pages, "production");

            Assert.AreEqual(BuilderMenu.TabButtonName("logistics"),
                TutorialPulseResolver.Resolve(root, TutorialPulseResolver.PlaceButtonName("conveyor"))?.name);
            BuilderMenu.ShowTab(bar, pages, "logistics");
            Assert.AreSame(place, TutorialPulseResolver.Resolve(root, TutorialPulseResolver.PlaceButtonName("conveyor")));
        }
    }
}
