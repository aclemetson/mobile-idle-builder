using System.Linq;
using System.Xml.Linq;
using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    [TestFixture]
    public class StatsSubControllerTests
    {
        [Test]
        public void IdleProjection_MultipliesRateAndAdBoost()
        {
            Assert.AreEqual(20.0, StatsSubController.IdleProjection(100, 0.2f, 1f), 0.0001);
            Assert.AreEqual(30.0, StatsSubController.IdleProjection(100, 0.2f, 1.5f), 0.0001);
        }

        [Test]
        public void FormatCap_ShowsHoursAndMinutes()
        {
            Assert.AreEqual("2h 0m", StatsSubController.FormatCap(7200f));
            Assert.AreEqual("0h 45m", StatsSubController.FormatCap(2700f));
        }

        [Test]
        public void IdleSubtitle_NotesAdBoost()
        {
            StringAssert.DoesNotContain("ad boost", StatsSubController.IdleSubtitle(0.2f, 1f));
            StringAssert.Contains("ad boost active", StatsSubController.IdleSubtitle(0.2f, 1.5f));
        }

        [TestCase("btn-stats")]
        [TestCase("stats-panel")]
        [TestCase("btn-close-stats")]
        public void GameHudUxml_HasStatsElement(string name)
        {
            var doc = XDocument.Load("Assets/UI/GameHUD.uxml");
            Assert.IsTrue(doc.Descendants().Any(e => (string)e.Attribute("name") == name),
                $"{name} missing from GameHUD.uxml");
        }
    }
}
