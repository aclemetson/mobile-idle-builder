using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Strings, the tournament currency (#500, part 1): paid at settlement per rank band and league, applied once
    /// on claim, kept through save/load and prestige, and named in the panel texts.
    /// </summary>
    public class StringsCurrencyTests
    {
        static PveReward Reward(int crystals = 150, int stars = 5, int strings = 60) =>
            new PveReward { week = "2026-11-27", league = "bronze", rank = 1, size = 10,
                            crystals = crystals, stars = stars, strings = strings };

        [Test]
        public void Claim_AddsStrings_Once()
        {
            var save = new SaveData { pveStrings = 10 };
            TournamentRules.ApplyReward(save, Reward(), new List<string> { "carbon_12" }, new System.Random(1));
            Assert.AreEqual(70, save.pveStrings);
            TournamentRules.ApplyReward(save, Reward(), new List<string> { "carbon_12" }, new System.Random(1));
            Assert.AreEqual(70, save.pveStrings, "the same week never pays twice");
        }

        [Test]
        public void Balance_SurvivesASave_AndPrestige()
        {
            var save = new SaveData { pveStrings = 123 };
            var loaded = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(save));
            loaded.currentRun = new CurrentRunData();   // what prestige does
            Assert.AreEqual(123, loaded.pveStrings);
        }

        [Test]
        public void Texts_NameStrings_AndSkipZeroParts()
        {
            Assert.AreEqual("Claim 150 ◆ + 5 ★ + 60 Strings", TournamentPanelText.ClaimButtonText(Reward()));
            Assert.AreEqual("Claim 5 ★", TournamentPanelText.ClaimButtonText(Reward(0, 5, 0)));
            Assert.AreEqual("1 String", TournamentPanelText.StringsText(1));
            Assert.AreEqual("Strings: 1,200", TournamentPanelText.BalanceText(1200));
            StringAssert.Contains("+60 Strings",
                TournamentPanelText.RevealText(Reward(), new List<string>(), id => id, id => 0));
        }

        [Test]
        public void ImportedConfig_PaysStrings_InEveryBand()
        {
            var cfg = Resources.Load<GameConfigSO>("GameConfig") ?? FindConfig();
            Assert.IsNotNull(cfg, "GameConfigSO asset");
            foreach (var league in cfg.pve.leagues)
                foreach (var band in league.rewards)
                    Assert.Greater(band.strings, 0, league.id);
            Assert.AreEqual(300, cfg.pve.leagues[cfg.pve.leagues.Length - 1].rewards[0].strings, "Champion 1st");
        }

        static GameConfigSO FindConfig()
        {
#if UNITY_EDITOR
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:GameConfigSO"))
                return UnityEditor.AssetDatabase.LoadAssetAtPath<GameConfigSO>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
#endif
            return null;
        }
    }
}
