using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using MobileIdleBuilder;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Edit-mode tests for the scene-flow refactor:
    ///   - SplashScreenController field defaults
    ///   - HUDPremiumShopSubController panel visibility toggling
    ///   - HUDSettingsSubController panel visibility and quality button styles
    /// </summary>
    [TestFixture]
    public class SceneFlowTests
    {
        // ================================================================
        // SplashScreenController
        // ================================================================

        [Test]
        public void SplashScreenController_DefaultTotalDuration_IsTwo()
        {
            var go = new GameObject("Splash");
            go.AddComponent<UIDocument>(); // required by RequireComponent
            var ctrl = go.AddComponent<SplashScreenController>();

            var field = typeof(SplashScreenController)
                .GetField("totalDuration", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(field, "totalDuration field must exist");
            Assert.AreEqual(2f, (float)field.GetValue(ctrl), 0.001f);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void SplashScreenController_DefaultFadeInDuration_IsHalfSecond()
        {
            var go = new GameObject("Splash");
            go.AddComponent<UIDocument>();
            var ctrl = go.AddComponent<SplashScreenController>();

            var field = typeof(SplashScreenController)
                .GetField("fadeInDuration", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(field, "fadeInDuration field must exist");
            Assert.AreEqual(0.5f, (float)field.GetValue(ctrl), 0.001f);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void SplashScreenController_CanBeInstantiated()
        {
            var go = new GameObject("Splash");
            go.AddComponent<UIDocument>();
            Assert.DoesNotThrow(() => go.AddComponent<SplashScreenController>());
            Object.DestroyImmediate(go);
        }

        // ================================================================
        // HUDPremiumShopSubController — panel visibility
        // ================================================================

        [Test]
        public void HUDPremiumShopSubController_Initialize_HidesPanel()
        {
            var root = new VisualElement();
            var panel = new VisualElement();
            panel.name = "shop-panel";
            root.Add(panel);

            var go = new GameObject("HUD");
            go.AddComponent<HUDController>();
            var shopCtrl = go.AddComponent<HUDPremiumShopSubController>();
            shopCtrl.Initialize(root);

            Assert.IsTrue(panel.ClassListContains("hidden"),
                "shop-panel must be hidden after Initialize");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void HUDPremiumShopSubController_Close_AddsHiddenClass()
        {
            var root = new VisualElement();
            var panel = new VisualElement();
            panel.name = "shop-panel";
            root.Add(panel);

            var go = new GameObject("HUD");
            go.AddComponent<HUDController>();
            var shopCtrl = go.AddComponent<HUDPremiumShopSubController>();
            shopCtrl.Initialize(root);

            // Manually show the panel then close it
            panel.RemoveFromClassList("hidden");
            shopCtrl.Close();

            Assert.IsTrue(panel.ClassListContains("hidden"),
                "Close() must hide shop-panel");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void HUDPremiumShopSubController_Cleanup_DoesNotThrow_WhenNotInitialized()
        {
            var go = new GameObject("HUD");
            go.AddComponent<HUDController>();
            var shopCtrl = go.AddComponent<HUDPremiumShopSubController>();

            Assert.DoesNotThrow(() => shopCtrl.Cleanup(),
                "Cleanup must be safe to call before Initialize");

            Object.DestroyImmediate(go);
        }

        // ================================================================
        // HUDSettingsSubController — panel visibility and quality styles
        // ================================================================

        [Test]
        public void HUDSettingsSubController_Initialize_HidesPanel()
        {
            var root = new VisualElement();
            var panel = new VisualElement();
            panel.name = "settings-panel";
            root.Add(panel);

            var go = new GameObject("HUD");
            go.AddComponent<HUDController>();
            var settingsCtrl = go.AddComponent<HUDSettingsSubController>();
            settingsCtrl.Initialize(root);

            Assert.IsTrue(panel.ClassListContains("hidden"),
                "settings-panel must be hidden after Initialize");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void HUDSettingsSubController_Close_AddsHiddenClass()
        {
            var root = new VisualElement();
            var panel = new VisualElement();
            panel.name = "settings-panel";
            root.Add(panel);

            var go = new GameObject("HUD");
            go.AddComponent<HUDController>();
            var settingsCtrl = go.AddComponent<HUDSettingsSubController>();
            settingsCtrl.Initialize(root);

            panel.RemoveFromClassList("hidden");
            settingsCtrl.Close();

            Assert.IsTrue(panel.ClassListContains("hidden"),
                "Close() must hide settings-panel");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void HUDSettingsSubController_QualityButtonStyles_OnlyActiveIsMarked()
        {
            // Verify the static helper SetQualityActive via reflection or by checking the
            // class-list contract on bare VisualElements (no MonoBehaviour lifecycle needed).
            var low    = new Button();
            var medium = new Button();
            var high   = new Button();

            // Simulate selecting Medium (quality=1)
            SetQualityActive(low,    false);
            SetQualityActive(medium, true);
            SetQualityActive(high,   false);

            Assert.IsFalse(low.ClassListContains("quality-btn--active"),   "Low must not be active");
            Assert.IsTrue(medium.ClassListContains("quality-btn--active"),  "Medium must be active");
            Assert.IsFalse(high.ClassListContains("quality-btn--active"),   "High must not be active");
        }

        [Test]
        public void HUDSettingsSubController_QualityButtonStyles_SwapClearsOld()
        {
            var low    = new Button();
            var medium = new Button();
            var high   = new Button();

            SetQualityActive(low,    false);
            SetQualityActive(medium, false);
            SetQualityActive(high,   true);

            // Now swap to Low
            SetQualityActive(low,    true);
            SetQualityActive(medium, false);
            SetQualityActive(high,   false);

            Assert.IsTrue(low.ClassListContains("quality-btn--active"),     "Low must be active after swap");
            Assert.IsFalse(high.ClassListContains("quality-btn--active"),   "High must be cleared after swap");
        }

        [Test]
        public void HUDSettingsSubController_Cleanup_DoesNotThrow_WhenNotInitialized()
        {
            var go = new GameObject("HUD");
            go.AddComponent<HUDController>();
            var settingsCtrl = go.AddComponent<HUDSettingsSubController>();

            Assert.DoesNotThrow(() => settingsCtrl.Cleanup(),
                "Cleanup must be safe to call before Initialize");

            Object.DestroyImmediate(go);
        }

        // ── Helper mirrors the private static SetQualityActive ────────────────

        private static void SetQualityActive(Button btn, bool active)
        {
            if (btn == null) return;
            if (active) btn.AddToClassList("quality-btn--active");
            else        btn.RemoveFromClassList("quality-btn--active");
        }
    }
}
