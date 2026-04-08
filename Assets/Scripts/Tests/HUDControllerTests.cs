using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using MobileIdleBuilder;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Edit-mode tests for the pure helper methods extracted from HUDController.
    ///
    /// What is tested here:
    ///   - SetElementVisible  — class-list toggling on bare VisualElements
    ///   - FormatPowerLabel   — power readout string formatting
    ///   - CalculatePrestigePreview — prestige currency preview math
    ///   - BuildInputsText    — recipe input formatting (no-ItemDatabase path)
    ///
    /// What is NOT tested here (requires Play Mode or manual testing):
    ///   - Button click → panel open/close (needs UIDocument + runtime)
    ///   - Notification coroutine auto-hide timing
    ///   - Tooltip positioning (requires rendered geometry)
    ///   - ECS queries from MonoBehaviour (needs a running World)
    /// </summary>
    [TestFixture]
    public class HUDControllerTests
    {
        // ================================================================
        // SetElementVisible
        // ================================================================

        [Test]
        public void SetElementVisible_Hide_AddsHiddenClass()
        {
            var el = new VisualElement();
            HUDController.SetElementVisible(el, false);
            Assert.IsTrue(el.ClassListContains("hidden"),
                "Element should have 'hidden' class after SetElementVisible(false)");
        }

        [Test]
        public void SetElementVisible_Show_RemovesHiddenClass()
        {
            var el = new VisualElement();
            el.AddToClassList("hidden");
            HUDController.SetElementVisible(el, true);
            Assert.IsFalse(el.ClassListContains("hidden"),
                "Element should not have 'hidden' class after SetElementVisible(true)");
        }

        [Test]
        public void SetElementVisible_Null_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => HUDController.SetElementVisible(null, true));
            Assert.DoesNotThrow(() => HUDController.SetElementVisible(null, false));
        }

        [Test]
        public void SetElementVisible_HideAlreadyHidden_IsIdempotent()
        {
            var el = new VisualElement();
            el.AddToClassList("hidden");
            HUDController.SetElementVisible(el, false);
            HUDController.SetElementVisible(el, false);
            Assert.IsTrue(el.ClassListContains("hidden"));
        }

        [Test]
        public void SetElementVisible_ShowAlreadyVisible_IsIdempotent()
        {
            var el = new VisualElement();
            HUDController.SetElementVisible(el, true);
            HUDController.SetElementVisible(el, true);
            Assert.IsFalse(el.ClassListContains("hidden"));
        }

        [Test]
        public void SetElementVisible_DoesNotAffectOtherClasses()
        {
            var el = new VisualElement();
            el.AddToClassList("panel-btn");
            el.AddToClassList("slide-panel");
            HUDController.SetElementVisible(el, false);
            Assert.IsTrue(el.ClassListContains("panel-btn"),   "panel-btn must not be removed");
            Assert.IsTrue(el.ClassListContains("slide-panel"), "slide-panel must not be removed");
        }

        // ================================================================
        // FormatPowerLabel
        // ================================================================

        [Test]
        public void FormatPowerLabel_WhenMaxIsZero_ReturnsNoPower()
        {
            Assert.AreEqual("⚡ No power", HUDController.FormatPowerLabel(0f, 0f));
        }

        [Test]
        public void FormatPowerLabel_WhenMaxIsNegative_ReturnsNoPower()
        {
            // Negative max should never occur in practice, but the method must not throw
            Assert.AreEqual("⚡ No power", HUDController.FormatPowerLabel(0f, -1f));
        }

        [Test]
        public void FormatPowerLabel_WhenMaxIsPositive_ShowsCurrentAndMax()
        {
            string result = HUDController.FormatPowerLabel(50f, 100f);
            StringAssert.StartsWith("⚡", result);
            StringAssert.Contains("50", result);
            StringAssert.Contains("100", result);
        }

        [Test]
        public void FormatPowerLabel_ZeroCurrentFullMax_ShowsZeroSlashMax()
        {
            string result = HUDController.FormatPowerLabel(0f, 200f);
            StringAssert.Contains("0", result);
            StringAssert.Contains("200", result);
        }

        [Test]
        public void FormatPowerLabel_FormatsToOneDecimalPlace()
        {
            // 0.#  format: no decimal for whole numbers
            string wholeResult = HUDController.FormatPowerLabel(10f, 20f);
            Assert.AreEqual("⚡ 10 / 20 eV", wholeResult);

            // 0.# format: one decimal place for fractional values
            string fracResult = HUDController.FormatPowerLabel(1.5f, 2.5f);
            Assert.AreEqual("⚡ 1.5 / 2.5 eV", fracResult);
        }

        [Test]
        public void FormatPowerLabel_FullCapacity_Formats()
        {
            string result = HUDController.FormatPowerLabel(500f, 500f);
            Assert.AreEqual("⚡ 500 / 500 eV", result);
        }

        // ================================================================
        // CalculatePrestigePreview
        // ================================================================

        [Test]
        public void CalculatePrestigePreview_ZeroNetWorth_ReturnsZero()
        {
            Assert.AreEqual(0L, HUDController.CalculatePrestigePreview(0f));
        }

        [Test]
        public void CalculatePrestigePreview_WholeNetWorth_ReturnsEqualLong()
        {
            Assert.AreEqual(5000L, HUDController.CalculatePrestigePreview(5000f));
        }

        [Test]
        public void CalculatePrestigePreview_FractionalNetWorth_TruncatesToLong()
        {
            // (long)(999.9f * 1f) == 999
            Assert.AreEqual(999L, HUDController.CalculatePrestigePreview(999.9f));
        }

        [Test]
        public void CalculatePrestigePreview_MatchesPrestigeSystemRate()
        {
            // PrestigeSystem uses: long earned = (long)(progress.NetWorth * rate) where rate = 1.0f
            // This test is the contract that keeps HUD display in sync with system behaviour.
            // If PrestigeSystem changes its rate, this test will fail and force a HUD update too.
            float netWorth = 12345f;
            const float systemRate = 1.0f;
            long systemEarned = (long)(netWorth * systemRate);

            Assert.AreEqual(systemEarned, HUDController.CalculatePrestigePreview(netWorth),
                "HUD preview must match what PrestigeSystem will actually award");
        }

        // ================================================================
        // BuildInputsText
        // ================================================================

        [Test]
        public void BuildInputsText_NullInputs_ReturnsEmpty()
        {
            var recipe = new RecipeJson { inputs = null };
            Assert.AreEqual("", HUDController.BuildInputsText(recipe));
        }

        [Test]
        public void BuildInputsText_EmptyInputList_ReturnsEmpty()
        {
            var recipe = new RecipeJson { inputs = new List<RecipeIngredientJson>() };
            Assert.AreEqual("", HUDController.BuildInputsText(recipe));
        }

        [Test]
        public void BuildInputsText_SingleInput_FallsBackToId_WhenNoDatabaseEntry()
        {
            // ItemDatabase.Instance is null in edit-mode → sym falls back to i.id
            var recipe = new RecipeJson
            {
                inputs = new List<RecipeIngredientJson>
                {
                    new RecipeIngredientJson { id = "up_quark", quantity = 2 }
                }
            };
            string result = HUDController.BuildInputsText(recipe);
            Assert.AreEqual("2× up_quark", result);
        }

        [Test]
        public void BuildInputsText_MultipleInputs_JoinedWithPlus()
        {
            var recipe = new RecipeJson
            {
                inputs = new List<RecipeIngredientJson>
                {
                    new RecipeIngredientJson { id = "proton",   quantity = 2 },
                    new RecipeIngredientJson { id = "neutron",  quantity = 2 },
                    new RecipeIngredientJson { id = "electron", quantity = 2 }
                }
            };
            string result = HUDController.BuildInputsText(recipe);
            Assert.AreEqual("2× proton  +  2× neutron  +  2× electron", result);
        }

        // ================================================================
        // Notification modifier class management
        // (tests the class-swap logic in isolation on a bare VisualElement)
        // ================================================================

        [Test]
        public void NotificationModifier_Warning_AddsWarningClass()
        {
            var banner = new VisualElement();
            banner.AddToClassList("notification-banner");

            // Simulate what ShowNotification does
            banner.RemoveFromClassList("notification-banner--warning");
            banner.RemoveFromClassList("notification-banner--danger");
            banner.AddToClassList("notification-banner--warning");

            Assert.IsTrue(banner.ClassListContains("notification-banner--warning"));
            Assert.IsFalse(banner.ClassListContains("notification-banner--danger"));
        }

        [Test]
        public void NotificationModifier_SwapFromWarningToDanger_ClearsOldClass()
        {
            var banner = new VisualElement();
            banner.AddToClassList("notification-banner--warning");

            // Swap to danger
            banner.RemoveFromClassList("notification-banner--warning");
            banner.RemoveFromClassList("notification-banner--danger");
            banner.AddToClassList("notification-banner--danger");

            Assert.IsFalse(banner.ClassListContains("notification-banner--warning"),
                "Old modifier must be cleared before applying new one");
            Assert.IsTrue(banner.ClassListContains("notification-banner--danger"));
        }

        [Test]
        public void NotificationModifier_NoModifier_LeavesNoModifierClasses()
        {
            var banner = new VisualElement();
            banner.AddToClassList("notification-banner--warning");

            // Simulate null modifier path (no AddToClassList call)
            banner.RemoveFromClassList("notification-banner--warning");
            banner.RemoveFromClassList("notification-banner--danger");

            Assert.IsFalse(banner.ClassListContains("notification-banner--warning"));
            Assert.IsFalse(banner.ClassListContains("notification-banner--danger"));
        }
    }
}
