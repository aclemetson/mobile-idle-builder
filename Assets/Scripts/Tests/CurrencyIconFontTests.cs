using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace MobileIdleBuilder.Tests
{
    /// <summary>The currency symbols must come from our own font, so they look the same on every device (#165).</summary>
    [TestFixture]
    public class CurrencyIconFontTests
    {
        const string Symbols = "◈✦♦⚡";   // entropy, prestige, crystal, power

        static FontAsset LoadFontAsset()
        {
            var asset = AssetDatabase.LoadAssetAtPath<FontAsset>("Assets/Fonts/CurrencyIcons SDF.asset");
            Assert.IsNotNull(asset, "Run MobileIdleBuilder > Build Currency Icon Font");
            return asset;
        }

        [Test]
        public void FontAsset_HoldsEverySymbol_AndNeverChangesAtRuntime()
        {
            var asset = LoadFontAsset();
            foreach (char symbol in Symbols)
                Assert.IsTrue(asset.HasCharacter(symbol, false, false), $"U+{(int)symbol:X4} is not in the atlas");
            Assert.AreEqual(AtlasPopulationMode.Static, asset.atlasPopulationMode);
        }

        [Test]
        public void HudPanel_FallsBackToTheIconFont()
        {
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Data/settings/gamePanelSettings.asset");
            Assert.IsNotNull(panel.textSettings, "gamePanelSettings has no text settings");
            CollectionAssert.Contains(panel.textSettings.fallbackFontAssets, LoadFontAsset());
        }

        // NotInter, the HUD's default font, has U+25C6 itself, so that character never reaches the fallback
        // and would show its plain diamond rather than our gem.
        [TestCase("Assets/UI/GameHUD.uxml")]
        [TestCase("Assets/Scripts/UI/HUDController.cs")]
        [TestCase("Assets/Scripts/UI/HUDStatusBarController.cs")]
        [TestCase("Assets/Scripts/UI/HUDPremiumShopSubController.cs")]
        [TestCase("Assets/Scripts/UI/DailyEventsSubController.cs")]
        public void CrystalText_UsesTheSymbolInTheIconFont(string path) =>
            StringAssert.DoesNotContain("◆", File.ReadAllText(path));
    }
}
