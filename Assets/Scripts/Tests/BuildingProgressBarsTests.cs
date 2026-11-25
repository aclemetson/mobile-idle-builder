using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>#211: which buildings get a world-space progress bar, and how full it is.</summary>
    [TestFixture]
    public class BuildingProgressBarsTests
    {
        static bool Try(bool hasCol, CollectorData c, bool hasRec, RecipeProcessData r, out float f) =>
            ProgressBarRules.TryFraction(hasCol, c, hasRec, r, out f);

        [Test]
        public void Harvester_FollowsItsTimer_EvenWithARecipeComponent()
        {
            var col = new CollectorData { OutputRate = 2f, Timer = 0.25f };   // half of a 0.5 s cycle
            var rec = new RecipeProcessData { RecipeID = 3, CraftTime = 10f, Progress = 9f };
            Assert.IsTrue(Try(true, col, true, rec, out float f));
            Assert.AreEqual(0.5f, f, 1e-5f);
        }

        [Test]
        public void Crafter_FollowsItsCraft_AndShowsEmptyWhileWaiting()
        {
            Assert.IsTrue(Try(false, default, true, new RecipeProcessData { RecipeID = 3, CraftTime = 4f, Progress = 1f }, out float f));
            Assert.AreEqual(0.25f, f, 1e-5f);
            Assert.IsTrue(Try(false, default, true, new RecipeProcessData { RecipeID = 3, CraftTime = 4f }, out f));
            Assert.AreEqual(0f, f, "a crafter waiting for inputs shows an empty bar");
        }

        [Test]
        public void NoBar_WithoutARecipeOrProduction()
        {
            Assert.IsFalse(Try(false, default, true, new RecipeProcessData { RecipeID = -1, CraftTime = 4f }, out _), "no recipe set");
            Assert.IsFalse(Try(false, default, true, new RecipeProcessData { RecipeID = 2, CraftTime = 0f }, out _), "no craft time");
            Assert.IsFalse(Try(false, default, false, default, out _), "generator, Filter, ...");
        }

        [Test]
        public void Fraction_IsClamped()
        {
            Try(true, new CollectorData { OutputRate = 1f, Timer = 3f }, false, default, out float f);
            Assert.AreEqual(1f, f);
            Try(false, default, true, new RecipeProcessData { RecipeID = 1, CraftTime = 2f, Progress = -1f }, out f);
            Assert.AreEqual(0f, f);
        }

        [Test]
        public void Fill_GrowsFromTheLeftEdge()
        {
            var (offset, width) = ProgressBarRules.Fill(1f, 0.25f);
            Assert.AreEqual(0.25f, width, 1e-5f);
            Assert.AreEqual(-0.375f, offset, 1e-5f, "left edge stays at -0.5");
            Assert.AreEqual((0f, 1f), ProgressBarRules.Fill(1f, 1f));
        }

        [Test]
        public void TheSettingDefaultsOn() => Assert.IsTrue(new SettingsData().showProgressBars);
    }
}
