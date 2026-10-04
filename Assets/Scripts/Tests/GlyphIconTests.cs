using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// GlyphIcon replaces the ✕ ✓ ↻ ⇄ text glyphs that render blank on Android (#165).
    /// Covers the code helpers and that the runtime HUD layout no longer depends on those glyphs.
    /// </summary>
    [TestFixture]
    public class GlyphIconTests
    {
        const string HudUxmlPath = "Assets/UI/GameHUD.uxml";
        static readonly char[] MissingOnAndroid = { '✕', '✓', '↻', '⇄' };

        [Test]
        public void SetOn_ClearsHostText_AndAddsOneIcon()
        {
            var btn = new Button { text = "✕" };

            var icon = GlyphIcon.SetOn(btn, GlyphIcon.Kind.Close);

            Assert.AreEqual(string.Empty, btn.text, "the glyph text must not be drawn by the font");
            Assert.AreEqual(GlyphIcon.Kind.Close, icon.Icon);
            Assert.AreEqual(1, btn.Query<GlyphIcon>().ToList().Count);
            Assert.IsTrue(btn.ClassListContains(GlyphIcon.HostUssClass));
            Assert.IsTrue(icon.ClassListContains(GlyphIcon.UssClass));
            Assert.AreEqual(PickingMode.Ignore, icon.pickingMode, "taps must reach the host button");
        }

        [Test]
        public void SetOn_Twice_RetargetsExistingIcon()
        {
            var btn = new Button();
            GlyphIcon.SetOn(btn, GlyphIcon.Kind.Close);
            GlyphIcon.SetOn(btn, GlyphIcon.Kind.Check);

            var icons = btn.Query<GlyphIcon>().ToList();
            Assert.AreEqual(1, icons.Count, "repeated calls (e.g. dialogue line changes) must not stack icons");
            Assert.AreEqual(GlyphIcon.Kind.Check, icons[0].Icon);
        }

        [Test]
        public void ClearFrom_RemovesIcon_SoTextCanReturn()
        {
            var btn = new Button();
            GlyphIcon.SetOn(btn, GlyphIcon.Kind.Check);

            GlyphIcon.ClearFrom(btn);
            btn.text = "▶";

            Assert.IsNull(btn.Q<GlyphIcon>());
            Assert.IsFalse(btn.ClassListContains(GlyphIcon.HostUssClass));
            Assert.AreEqual("▶", btn.text);
        }

        [Test]
        public void TitleRow_PutsIconBeforeTitle()
        {
            var title = new Label("Stage 1");
            var row   = GlyphIcon.TitleRow(GlyphIcon.Kind.Check, title);

            Assert.AreEqual(2, row.childCount);
            Assert.IsInstanceOf<GlyphIcon>(row[0]);
            Assert.AreSame(title, row[1]);
        }

        [Test]
        public void GameHUD_HasNoAndroidMissingGlyphs_AndIconsResolve()
        {
            var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(HudUxmlPath);
            Assert.IsNotNull(asset, $"{HudUxmlPath} must load");

            var root = asset.Instantiate();

            var offenders = root.Query<TextElement>().ToList()
                                .Where(t => t.text != null && t.text.IndexOfAny(MissingOnAndroid) >= 0)
                                .Select(t => t.name)
                                .ToList();
            CollectionAssert.IsEmpty(offenders, "these elements still rely on a glyph the Android font lacks");

            Assert.Greater(root.Query<GlyphIcon>().ToList().Count, 20, "GlyphIcon elements must resolve from UXML");

            var close = root.Q<Button>("btn-close-settings");
            Assert.IsNotNull(close);
            Assert.AreEqual(GlyphIcon.Kind.Close, close.Q<GlyphIcon>()?.Icon);

            Assert.AreEqual(GlyphIcon.Kind.Check,  root.Q<Button>("btn-confirm-place").Q<GlyphIcon>()?.Icon);
            Assert.AreEqual(GlyphIcon.Kind.Rotate, root.Q<Button>("btn-rotate-output").Q<GlyphIcon>()?.Icon);
            Assert.AreEqual(GlyphIcon.Kind.Flip,   root.Q<Button>("btn-flip-building").Q<GlyphIcon>()?.Icon);
        }
    }
}
