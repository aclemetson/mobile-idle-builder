using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Builder menu layout (#487): role tabs over a horizontally scrolling row of building cards. Each card shows
    /// the building's picture (a render from BuildingIconRenderer, else its main product's icon, else the Build
    /// glyph), its name, price and a Place button, with an (i) button for the details. Pure helpers and the card /
    /// tab factories live here; HUDController.BuildBuildingsList fills them.
    ///
    /// Tab pages carry the <see cref="PageClass"/> class and are named "builder-page-{tab}" beside the tab buttons
    /// "builder-tab-{tab}", so TutorialPulseResolver can guide to a hidden tab first.
    /// </summary>
    public static class BuilderMenu
    {
        public const string PageClass = MenuTabs.PageClass;
        const string Prefix = "builder";

        public static readonly string[] Tabs = { "harvest", "production", "logistics", "power", "special" };

        public static string TabLabel(string tab) => tab switch
        {
            "harvest"    => "Harvest",
            "production" => "Production",
            "logistics"  => "Logistics",
            "power"      => "Power",
            "special"    => "Special",
            _            => tab,
        };

        public static string PageName(string tab) => MenuTabs.PageName(Prefix, tab);
        public static string TabButtonName(string tab) => MenuTabs.TabButtonName(Prefix, tab);

        /// <summary>
        /// Which tab a building goes under: sinks are Special; splitters, filters and belts Logistics; generators
        /// and relays Power; anything placed on a field or drawing from a source (harvesters, the Spring) Harvest;
        /// everything else (the crafters) Production.
        /// </summary>
        public static string TabOf(BuildingSO b)
        {
            if (b == null) return "logistics";
            if (b.isEntropySink) return "special";
            if (b.isItemFilter) return "logistics";
            if (b.isPowerSource || b.structureKind == BuildingStructureKind.PowerRelay) return "power";
            if (b.isSpring || b.structureKind == BuildingStructureKind.NucleonHarvester
                || (b.compatibleFields != null && b.compatibleFields.Length > 0)) return "harvest";
            return "production";
        }

        /// <summary>The card's picture: the rendered building, else what it makes, else null (the caller draws a glyph).</summary>
        public static Sprite CardSprite(BuildingSO b, RecipeSO defaultRecipe)
        {
            if (b == null) return null;
            if (b.icon != null) return b.icon;
            if (defaultRecipe != null && defaultRecipe.outputItem != null && defaultRecipe.outputItem.icon != null)
                return defaultRecipe.outputItem.icon;
            if (b.supportedRecipes != null)
                foreach (var r in b.supportedRecipes)
                    if (r != null && r.outputItem != null && r.outputItem.icon != null) return r.outputItem.icon;
            return null;
        }

        /// <summary>The tab to show: the remembered one if it has cards, else the first tab that has any.</summary>
        public static string PickTab(string remembered, ICollection<string> tabsWithCards) =>
            MenuTabs.PickTab(remembered, tabsWithCards, Tabs);

        /// <summary>
        /// A building card: picture (sprite, or <paramref name="glyph"/> when there is none), name, cost and the
        /// Place button; the (i) button sits in the top-right corner. Keeps the "building-card" class the tutorial
        /// highlights.
        /// </summary>
        public static VisualElement MakeCard(string name, Sprite picture, GlyphIcon.Kind glyph, Label costLabel,
                                             Button placeBtn, Button infoBtn)
        {
            var card = new VisualElement();
            card.AddToClassList("building-card");
            card.AddToClassList("building-tile");

            var pic = new VisualElement();
            pic.AddToClassList("building-tile__picture");
            if (picture != null) pic.style.backgroundImage = new StyleBackground(picture);
            else
            {
                var g = new GlyphIcon(glyph);
                g.AddToClassList("building-tile__glyph");
                pic.Add(g);
            }
            card.Add(pic);

            var nameLabel = new Label(name);
            nameLabel.AddToClassList("building-card-name");
            nameLabel.AddToClassList("building-tile__name");
            card.Add(nameLabel);

            costLabel.AddToClassList("building-tile__cost");
            card.Add(costLabel);

            placeBtn.AddToClassList("craft-btn");
            placeBtn.AddToClassList("building-card-place");
            placeBtn.AddToClassList("building-tile__place");
            card.Add(placeBtn);

            if (infoBtn != null)
            {
                GlyphIcon.SetOn(infoBtn, GlyphIcon.Kind.Info);
                infoBtn.AddToClassList("building-tile__info");
                card.Add(infoBtn);
            }
            return card;
        }

        /// <summary>The tab bar (one button per tab, named for TutorialPulseResolver).</summary>
        public static VisualElement MakeTabBar(Action<string> onSelect) =>
            MenuTabs.MakeTabBar(Prefix, Tabs, TabLabel, onSelect);

        /// <summary>Shows <paramref name="tab"/>'s page, marks its button, hides tabs with no cards.</summary>
        public static void ShowTab(VisualElement bar, IReadOnlyDictionary<string, VisualElement> pages, string tab) =>
            MenuTabs.ShowTab(Prefix, bar, pages, tab);
    }
}
