using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// #504: the "Choose your name" card sits in the upper part of the screen so the on-screen keyboard, which
    /// opens from the bottom (about 40% of a portrait phone), doesn't cover it. Lays out the real GameHUD.uxml.
    /// </summary>
    public class NamePromptLayoutTests
    {
        class Host : EditorWindow { }

        Host _window;

        [TearDown]
        public void TearDown()
        {
            if (_window != null) _window.Close();
        }

        [UnityTest]
        public IEnumerator NameCard_StaysAboveTheKeyboard_InPortrait()
        {
            // Lays out in a real editor window, which needs a graphics device: CI runs Unity with -nographics
            // (opening the window there logs an error and fails the run). Checked in the Editor / on GPU runners.
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("Needs a graphics device to lay out an editor window (CI runs -nographics).");

            const float W = 540, H = 960;
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/GameHUD.uxml");
            _window = ScriptableObject.CreateInstance<Host>();
            _window.position = new Rect(50, 50, W, H);
            _window.ShowUtility();
            var root = tree.Instantiate();
            root.style.width = W; root.style.height = H;
            _window.rootVisualElement.Add(root);

            var modal = root.Q("name-prompt");
            HUDController.SetElementVisible(modal, true);
            for (int f = 0; f < 6; f++) { _window.Repaint(); yield return null; }

            var card = modal.Q(className: "idle-return-card");
            var rect = card.worldBound;
            Debug.Log($"[#504] modal {modal.worldBound} card {rect}");
            Assert.Greater(rect.height, 100f, "card laid out");
            Assert.LessOrEqual(rect.yMax, H * 0.6f, "the card's bottom must stay above the keyboard (lower ~40%)");
            Assert.GreaterOrEqual(rect.yMin, H * 0.05f, "and below the top edge");
        }
    }
}
