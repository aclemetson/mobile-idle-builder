using System.Collections.Generic;
using NUnit.Framework;
using Unity.Entities;

namespace MobileIdleBuilder.Tests
{
    /// <summary>#334: a belt segment's sprite follows the item on it, not the segment.</summary>
    [TestFixture]
    public class ConveyorVisualStaleTests
    {
        static readonly Entity Segment = new Entity { Index = 7, Version = 1 };

        [Test]
        public void SameItem_KeepsItsVisual()
        {
            var shown = new Dictionary<Entity, int> { [Segment] = 3 };
            Assert.IsFalse(ConveyorVisualizer.VisualIsStale(shown, Segment, 3));
        }

        [Test]
        public void ItemSwappedInPlace_RebuildsTheVisual()
        {
            // A Proton leaves and a Neutron arrives on the same segment between two visual updates.
            var shown = new Dictionary<Entity, int> { [Segment] = 3 };
            Assert.IsTrue(ConveyorVisualizer.VisualIsStale(shown, Segment, 4));
        }

        [Test]
        public void UnknownSegment_GetsAVisual()
        {
            Assert.IsTrue(ConveyorVisualizer.VisualIsStale(new Dictionary<Entity, int>(), Segment, 3));
        }
    }
}
