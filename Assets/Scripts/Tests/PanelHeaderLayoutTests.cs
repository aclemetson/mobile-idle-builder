using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// #499: a list taller than its slide panel used to squeeze the panel header (it shrank with the list), and
    /// the title vanished, as in Research list mode. <c>.panel-header { flex-shrink: 0 }</c> keeps it whole.
    /// Lays out the real GameHUD.uxml in an editor window, so it needs a graphics device.
    /// </summary>
    public class PanelHeaderLayoutTests
    {
        [TestCase("research-panel", "research-list")]
        [TestCase("pve-panel", "pve-page-tournament")]
        public void Header_KeepsItsHeight_UnderATallList(string panelName, string listName)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("Needs a graphics device to lay out UI (CI runs -nographics).");

            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/GameHUD.uxml");
            var win = ScriptableObject.CreateInstance<EditorWindow>();
            win.position = new Rect(100, 100, 540, 960);
            win.ShowUtility();
            try
            {
                var root = tree.Instantiate();
                root.style.width = 540; root.style.height = 960;
                win.rootVisualElement.Add(root);
                var panel = root.Q(panelName);
                panel.RemoveFromClassList("hidden");
                var header = panel.Q("panel-header");
                var anchor = root.Q(listName);
                var parent = anchor.parent;
                for (int i = 0; i < 60; i++)
                {
                    var row = new Label("row " + i);
                    row.style.height = 60;
                    parent.Insert(parent.IndexOf(anchor), row);
                }
                var repaint = typeof(EditorWindow).GetMethod("RepaintImmediately",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                repaint?.Invoke(win, null);
                repaint?.Invoke(win, null);

                var title = header.Q<Label>(className: "panel-title");
                Assert.Greater(header.layout.height, 50f, "header squeezed by the list");
                Assert.LessOrEqual(title.layout.height, header.layout.height, "title fits inside its header");
            }
            finally
            {
                win.Close();
            }
        }
    }
}
