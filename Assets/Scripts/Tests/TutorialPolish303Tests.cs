using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    [TestFixture]
    public class TutorialPolish303Tests
    {
        // ---- ClampPopup (item 1) ----
        static readonly Vector2 Panel = new Vector2(400f, 800f);
        static readonly Vector2 Size  = new Vector2(100f, 50f);

        [Test]
        public void ClampPopup_Inside_Unchanged()
        {
            var p = HUDController.ClampPopup(new Vector2(150f, 300f), Size, Panel, 8f);
            Assert.AreEqual(new Vector2(150f, 300f), p);
        }

        [Test]
        public void ClampPopup_LeftEdge_ClampsToMargin()
        {
            var p = HUDController.ClampPopup(new Vector2(-30f, 300f), Size, Panel, 8f);
            Assert.AreEqual(8f, p.x);
        }

        [Test]
        public void ClampPopup_RightEdge_ClampsToPanelMinusSize()
        {
            var p = HUDController.ClampPopup(new Vector2(390f, 300f), Size, Panel, 8f);
            Assert.AreEqual(400f - 100f - 8f, p.x);
        }

        [Test]
        public void ClampPopup_TopEdge_ClampsToMargin()
        {
            var p = HUDController.ClampPopup(new Vector2(150f, -20f), Size, Panel, 8f);
            Assert.AreEqual(8f, p.y);
        }

        // ---- IsTap (item 3) ----
        [Test]
        public void IsTap_SmallMovement_IsTap()
            => Assert.IsTrue(HUDController.IsTap(new Vector2(5f, 5f), new Vector2(8f, 7f), 12f));

        [Test]
        public void IsTap_LargeMovement_IsPan()
            => Assert.IsFalse(HUDController.IsTap(new Vector2(5f, 5f), new Vector2(40f, 5f), 12f));

        // ---- DropBag (item 6) ----
        static Func<int, int> Rng(int seed) { var r = new System.Random(seed); return r.Next; }

        [Test]
        public void DropBag_EvenWeights_GivesEqualCounts()
        {
            var bag = new DropBag(new[] { 0.5f, 0.5f }, Rng(1));
            var counts = new int[2];
            for (int i = 0; i < 20; i++) counts[bag.Next()]++;
            Assert.AreEqual(10, counts[0]);
            Assert.AreEqual(10, counts[1]);
        }

        [Test]
        public void DropBag_EvenWeights_EveryPairHasOneOfEach()
        {
            var bag = new DropBag(new[] { 0.5f, 0.5f }, Rng(7));
            for (int i = 0; i < 10; i++)
                Assert.AreEqual(1, bag.Next() + bag.Next());
        }

        [Test]
        public void DropBag_ThreeToOne_GivesThreeToOne()
        {
            var bag = new DropBag(new[] { 3f, 1f }, Rng(3));
            var counts = new int[2];
            for (int i = 0; i < 40; i++) counts[bag.Next()]++;
            Assert.AreEqual(30, counts[0]);
            Assert.AreEqual(10, counts[1]);
        }

        [Test]
        public void DropBag_SingleDrop_AlwaysReturnsIt()
        {
            var bag = new DropBag(new[] { 1f }, Rng(5));
            for (int i = 0; i < 5; i++) Assert.AreEqual(0, bag.Next());
        }

        [Test]
        public void DropBag_NonIntegerRatio_NotUsable()
            => Assert.IsFalse(new DropBag(new[] { 1f, 1.5f }, Rng(1)).IsUsable);

        // ---- Hints mention Destroy (item 5) ----
        [Test]
        public void BeltRemovalHints_MentionDestroy()
        {
            string json = File.ReadAllText(Path.Combine(Application.dataPath, "Data/game_data.json"));
            int idx = json.IndexOf("belt tool: switch Create to Destroy", StringComparison.Ordinal);
            Assert.GreaterOrEqual(idx, 0);
            Assert.GreaterOrEqual(json.IndexOf("belt tool: switch Create to Destroy", idx + 1, StringComparison.Ordinal), 0,
                "both reroute hints should mention the belt tool's Destroy mode");
        }
    }
}
