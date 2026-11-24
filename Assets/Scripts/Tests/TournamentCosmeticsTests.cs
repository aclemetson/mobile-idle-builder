using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// #500 part 3: cosmetics bought with Strings. Name colours and titles go to the leaderboard (server-validated
    /// ids), belt themes recolour your own belts.
    /// </summary>
    public class TournamentCosmeticsTests
    {
        [Test]
        public void Catalogue_MatchesThePlan()
        {
            int colors = 0, titles = 0, belts = 0;
            foreach (var d in PveCosmetics.All)
            {
                switch (d.kind)
                {
                    case CosmeticKind.NameColor: colors++; Assert.AreEqual(25, d.cost, d.id); break;
                    case CosmeticKind.Title:     titles++; Assert.AreEqual(40, d.cost, d.id); break;
                    default:                     belts++;  Assert.AreEqual(100, d.cost, d.id); break;
                }
                Assert.AreSame(d, PveCosmetics.Find(d.id), "ids are unique");
            }
            Assert.AreEqual((6, 6, 3), (colors, titles, belts));
        }

        [Test]
        public void Buy_SpendsStrings_OwnsAndEquips_OnlyOnce()
        {
            var save = new SaveData { pveStrings = 60 };
            Assert.IsTrue(TournamentCosmetics.TryBuy(save, "color_gold"));
            Assert.AreEqual(35, save.pveStrings);
            Assert.IsTrue(TournamentCosmetics.Owns(save, "color_gold"));
            Assert.AreEqual("color_gold", save.pveNameColor, "a purchase equips it");
            Assert.IsFalse(TournamentCosmetics.TryBuy(save, "color_gold"), "already owned");
            Assert.IsFalse(TournamentCosmetics.TryBuy(save, "title_string_theorist"), "40 > 35");
            Assert.IsFalse(TournamentCosmetics.TryBuy(save, "nope"));
            Assert.AreEqual(35, save.pveStrings);
        }

        [Test]
        public void Equip_TogglesOnePerKind()
        {
            var save = new SaveData { pveCosmetics = new List<string> { "color_gold", "color_rose", "belt_plasma" } };
            Assert.IsTrue(TournamentCosmetics.ToggleEquip(save, "color_gold"));
            Assert.IsTrue(TournamentCosmetics.ToggleEquip(save, "color_rose"));
            Assert.AreEqual("color_rose", save.pveNameColor, "one colour at a time");
            Assert.IsTrue(TournamentCosmetics.ToggleEquip(save, "color_rose"));
            Assert.IsNull(save.pveNameColor, "tapping the equipped one takes it off");
            Assert.IsFalse(TournamentCosmetics.ToggleEquip(save, "title_fusion_fanatic"), "not owned");
            Assert.IsTrue(TournamentCosmetics.ToggleEquip(save, "belt_plasma"));
            Assert.IsNull(save.pveNameColor);
            Assert.AreEqual("belt_plasma", save.pveBeltTheme);
        }

        [Test]
        public void BeltTheme_HasColours_DefaultIsNone()
        {
            Assert.IsNull(TournamentCosmetics.BeltColors(new SaveData()));
            var theme = TournamentCosmetics.BeltColors(new SaveData { pveBeltTheme = "belt_aurora" });
            Assert.IsTrue(theme.HasValue);
            Assert.Greater(theme.Value.glow.g, theme.Value.glow.r, "aurora glows green");
            Assert.IsNull(TournamentCosmetics.BeltColors(new SaveData { pveBeltTheme = "color_gold" }), "wrong kind");
        }

        [Test]
        public void LookDiffers_ComparesWithTheServer_TreatingNullAsNone()
        {
            var save = new SaveData { pveNameColor = "color_gold" };
            Assert.IsTrue(TournamentCosmetics.LookDiffers(save, "", ""));
            Assert.IsFalse(TournamentCosmetics.LookDiffers(save, "color_gold", null));
            Assert.IsFalse(TournamentCosmetics.LookDiffers(new SaveData(), null, ""));
        }

        [Test]
        public void Ownership_SurvivesASave_AndPrestige()
        {
            var save = new SaveData { pveCosmetics = new List<string> { "title_idle_physicist" }, pveTitle = "title_idle_physicist" };
            var loaded = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(save));
            loaded.currentRun = new CurrentRunData();
            Assert.IsTrue(TournamentCosmetics.Owns(loaded, "title_idle_physicist"));
            Assert.AreEqual("title_idle_physicist", loaded.pveTitle);
        }

        [Test]
        public void Texts()
        {
            Assert.AreEqual("25 Strings", TournamentPanelText.CosmeticButtonText(false, false, 25));
            Assert.AreEqual("Equip", TournamentPanelText.CosmeticButtonText(true, false, 25));
            Assert.AreEqual("Equipped", TournamentPanelText.CosmeticButtonText(true, true, 25));
            Assert.IsTrue(TournamentCosmetics.NameColor("color_cyan").HasValue);
            Assert.IsFalse(TournamentCosmetics.NameColor("title_fusion_fanatic").HasValue);
        }
    }
}
