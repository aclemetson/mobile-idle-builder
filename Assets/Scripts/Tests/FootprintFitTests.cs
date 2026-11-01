using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// FootprintFit restores a saved building whose footprint grew (the Atom Generator went 1x1 -> 2x1
    /// in #275) without landing it on a neighbour.
    /// </summary>
    [TestFixture]
    public class FootprintFitTests
    {
        static readonly Vector2Int TwoByOne = new(2, 1);

        [Test]
        public void Cells_RotationSwapsAxes()
        {
            CollectionAssert.AreEquivalent(new[] { new Vector2Int(3, 3), new Vector2Int(4, 3) },
                FootprintFit.Cells(new Vector2Int(3, 3), TwoByOne, 0).ToArray());
            CollectionAssert.AreEquivalent(new[] { new Vector2Int(3, 3), new Vector2Int(3, 4) },
                FootprintFit.Cells(new Vector2Int(3, 3), TwoByOne, 1).ToArray());
        }

        [Test]
        public void TryFit_FreeSpace_KeepsSavedAnchor()
        {
            Assert.IsTrue(FootprintFit.TryFit(new Vector2Int(3, 3), TwoByOne, 0, _ => true, out var at));
            Assert.AreEqual(new Vector2Int(3, 3), at);
        }

        [Test]
        public void TryFit_NeighbourOnTheRight_ShiftsLeftSoOldCellIsTheFarEnd()
        {
            var taken = new HashSet<Vector2Int> { new(4, 3) };
            Assert.IsTrue(FootprintFit.TryFit(new Vector2Int(3, 3), TwoByOne, 0, c => !taken.Contains(c), out var at));
            Assert.AreEqual(new Vector2Int(2, 3), at);
        }

        [Test]
        public void TryFit_BoxedIn_Fails()
        {
            var taken = new HashSet<Vector2Int> { new(2, 3), new(4, 3) };
            Assert.IsFalse(FootprintFit.TryFit(new Vector2Int(3, 3), TwoByOne, 0, c => !taken.Contains(c), out _));
        }
    }
}
