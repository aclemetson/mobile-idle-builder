using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// The tutorial hint banner can be minimized to a "Show hint" pill so it stops covering
    /// menus. Runs against the real GameHUD.uxml so a renamed element fails here, not on device.
    /// </summary>
    [TestFixture]
    public class TutorialHintMinimizeTests
    {
        const string HudUxmlPath = "Assets/UI/GameHUD.uxml";

        GameObject          _go;
        HUDBannerController _banner;
        VisualElement       _hint;
        Button              _restore;

        [SetUp]
        public void SetUp()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(HudUxmlPath);
            Assert.IsNotNull(tree, $"{HudUxmlPath} not found");
            var root = tree.CloneTree();

            _hint    = root.Q("tutorial-hint-banner");
            _restore = root.Q<Button>("tutorial-hint-restore");
            Assert.IsNotNull(_hint, "tutorial-hint-banner missing from GameHUD.uxml");
            Assert.IsNotNull(_restore, "tutorial-hint-restore missing from GameHUD.uxml");
            Assert.IsNotNull(root.Q<Button>("tutorial-hint-banner__minimize"),
                "tutorial-hint-banner__minimize missing from GameHUD.uxml");

            _go     = new GameObject("HUDBannerControllerTest");
            _banner = _go.AddComponent<HUDBannerController>();
            _banner.Init(root);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_go);

        bool HintVisible    => !_hint.ClassListContains("hidden");
        bool RestoreVisible => !_restore.ClassListContains("hidden");

        [Test]
        public void Init_NoHint_BothHidden()
        {
            Assert.IsFalse(HintVisible);
            Assert.IsFalse(RestoreVisible);
        }

        [Test]
        public void Minimize_SwapsBannerForPill_AndRestoreSwapsBack()
        {
            _banner.ShowTutorialHint("Build a harvester");

            _banner.MinimizeTutorialHint();
            Assert.IsFalse(HintVisible);
            Assert.IsTrue(RestoreVisible);
            Assert.IsTrue(_banner.IsTutorialHintMinimized);

            _banner.RestoreTutorialHint();
            Assert.IsTrue(HintVisible);
            Assert.IsFalse(RestoreVisible);
        }

        [Test]
        public void SameHintReshown_StaysMinimized()
        {
            _banner.ShowTutorialHint("Build a harvester");
            _banner.MinimizeTutorialHint();

            _banner.ShowTutorialHint("Build a harvester");

            Assert.IsFalse(HintVisible);
            Assert.IsTrue(RestoreVisible);
        }

        [Test]
        public void NewHintText_ReExpands()
        {
            _banner.ShowTutorialHint("Build a harvester");
            _banner.MinimizeTutorialHint();

            _banner.ShowTutorialHint("Place a conveyor");

            Assert.IsTrue(HintVisible, "a new tutorial step's hint must not stay hidden");
            Assert.IsFalse(RestoreVisible);
        }

        [Test]
        public void MenuOpen_CollapsesHintToPill_AndCloseExpandsIt()
        {
            _banner.ShowTutorialHint("Place an Atom Generator");

            _banner.SetMenuOpen(true);
            Assert.IsFalse(HintVisible, "the banner must not sit on the open drawer's bottom rows");
            Assert.IsTrue(RestoreVisible);

            _banner.SetMenuOpen(false);
            Assert.IsTrue(HintVisible);
            Assert.IsFalse(RestoreVisible);
        }

        [Test]
        public void MenuOpen_PlayerMinimized_StaysMinimizedAfterClose()
        {
            _banner.ShowTutorialHint("Place an Atom Generator");
            _banner.MinimizeTutorialHint();

            _banner.SetMenuOpen(true);
            _banner.SetMenuOpen(false);

            Assert.IsFalse(HintVisible, "closing a menu must not undo the player's own minimize");
            Assert.IsTrue(RestoreVisible);
        }

        [Test]
        public void NewHintWhileMenuOpen_StaysCollapsed()
        {
            _banner.SetMenuOpen(true);

            _banner.ShowTutorialHint("Rotate the Atom Generator");

            Assert.IsFalse(HintVisible);
            Assert.IsTrue(RestoreVisible);
        }

        [Test]
        public void HideWhileMinimized_HidesPillToo()
        {
            _banner.ShowTutorialHint("Build a harvester");
            _banner.MinimizeTutorialHint();

            _banner.HideTutorialHint();

            Assert.IsFalse(HintVisible);
            Assert.IsFalse(RestoreVisible, "the pill must not outlive the hint");
            Assert.IsFalse(_banner.IsTutorialHintMinimized);
        }

        [Test]
        public void Progress_ShowsUnderHint_AndNullHidesIt()
        {
            var progress = _hint.Q<Label>("tutorial-hint-banner__progress");
            Assert.IsNotNull(progress, "tutorial-hint-banner__progress missing from the hint banner");
            _banner.ShowTutorialHint("Collect quarks");

            _banner.SetTutorialHintProgress("Up Quark 1/4");
            Assert.IsFalse(progress.ClassListContains("hidden"));
            Assert.AreEqual("Up Quark 1/4", progress.text);

            _banner.SetTutorialHintProgress(null);
            Assert.IsTrue(progress.ClassListContains("hidden"));
        }

        [Test]
        public void ProgressUpdate_DoesNotReExpandMinimizedHint()
        {
            _banner.ShowTutorialHint("Collect quarks");
            _banner.MinimizeTutorialHint();

            _banner.SetTutorialHintProgress("Up Quark 2/4");

            Assert.IsFalse(HintVisible, "a count changing on every collect tap must not undo the player's minimize");
            Assert.IsTrue(RestoreVisible);
        }
    }
}
