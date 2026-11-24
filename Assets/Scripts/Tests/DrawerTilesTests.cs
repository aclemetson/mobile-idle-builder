using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace MobileIdleBuilder.Tests
{
    /// <summary>#486: every side-drawer menu button is an icon tile with a caption, and keeps its name.</summary>
    public class DrawerTilesTests
    {
        static readonly string[] Buttons =
        {
            "btn-buildings", "btn-recipes", "btn-codex", "btn-stats", "btn-research", "btn-cyclotron", "btn-upgrades",
            "btn-managers", "btn-megastructure", "btn-rewards", "btn-achievements", "btn-worlds", "btn-sites",
            "btn-tournament", "btn-shop",
        };

        [Test]
        public void EveryDrawerButton_IsAnIconTileWithACaption()
        {
            var root = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/GameHUD.uxml").Instantiate();
            foreach (var name in Buttons)
            {
                var b = root.Q<Button>(name);
                Assert.IsNotNull(b, name);
                Assert.IsTrue(b.ClassListContains("drawer-tile"), $"{name} is not a tile");
                Assert.IsTrue(string.IsNullOrEmpty(b.text), $"{name} still has button text");
                Assert.IsNotNull(b.Q<GlyphIcon>(), $"{name} has no icon");
                Assert.IsFalse(string.IsNullOrEmpty(DrawerTile.Caption(b)?.text), $"{name} has no caption");
            }
        }

        [Test]
        public void EveryTile_HasItsOwnIcon()
        {
            var root = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/GameHUD.uxml").Instantiate();
            var seen = new System.Collections.Generic.HashSet<GlyphIcon.Kind>();
            foreach (var name in Buttons)
                Assert.IsTrue(seen.Add(root.Q<Button>(name).Q<GlyphIcon>().Icon), $"{name} reuses another tile's icon");
        }

        [Test]
        public void SetCaption_WritesTheCaption_OrTheTextOfAPlainButton()
        {
            var tile = new Button();
            var caption = new Label("old");
            caption.AddToClassList(DrawerTile.CaptionClass);
            tile.Add(caption);
            DrawerTile.SetCaption(tile, "Cyclotron (42m)");
            Assert.AreEqual("Cyclotron (42m)", caption.text);
            Assert.IsTrue(string.IsNullOrEmpty(tile.text));

            var plain = new Button();
            DrawerTile.SetCaption(plain, "x");
            Assert.AreEqual("x", plain.text);
        }
    }
}
