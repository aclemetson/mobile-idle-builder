using System.Linq;
using System.Xml.Linq;
using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Template instances in GameHUD.uxml that hold absolutely positioned overlays must themselves
    /// cover the screen. Left in the root's flex column, the notification instance was laid out
    /// below the grid area, so its banner sat off the bottom edge and only flashed into view while
    /// sliding in and out (#238).
    /// Reads the UXML attributes directly: a cloned tree outside a panel does not expose inline
    /// UXML styles through VisualElement.style.
    /// </summary>
    [TestFixture]
    public class HudOverlayInstanceTests
    {
        const string HudUxmlPath = "Assets/UI/GameHUD.uxml";

        [TestCase("notification-instance")]
        [TestCase("tooltip-instance")]
        [TestCase("dialogue-instance")]
        public void OverlayInstance_CoversTheScreen_AndLetsTapsThrough(string instanceName)
        {
            var doc      = XDocument.Load(HudUxmlPath);
            var instance = doc.Descendants()
                              .FirstOrDefault(e => e.Name.LocalName == "Instance" &&
                                                   (string)e.Attribute("name") == instanceName);
            Assert.IsNotNull(instance, $"{instanceName} missing from GameHUD.uxml");

            string style = ((string)instance.Attribute("style") ?? "").Replace(" ", "");
            foreach (var rule in new[] { "position:absolute", "left:0", "right:0", "top:0", "bottom:0" })
                StringAssert.Contains(rule, style, $"{instanceName} needs {rule}");
            Assert.AreEqual("Ignore", (string)instance.Attribute("picking-mode"),
                $"a full-screen {instanceName} must not swallow taps");
        }
    }
}
