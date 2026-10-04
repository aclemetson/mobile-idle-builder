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
        public void HideWhileMinimized_HidesPillToo()
        {
            _banner.ShowTutorialHint("Build a harvester");
            _banner.MinimizeTutorialHint();

            _banner.HideTutorialHint();

            Assert.IsFalse(HintVisible);
            Assert.IsFalse(RestoreVisible, "the pill must not outlive the hint");
            Assert.IsFalse(_banner.IsTutorialHintMinimized);
        }
    }
}
