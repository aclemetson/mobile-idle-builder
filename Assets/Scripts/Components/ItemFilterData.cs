using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>Marks a Filter building (#321): items pass straight from its input to whichever output side allows them.</summary>
    public struct ItemFilterTag : IComponentData { }

    /// <summary>
    /// One routing rule on a Filter: items of <see cref="ItemID"/> leave by the output port facing
    /// <see cref="Facing"/> (an <see cref="OutputDirection"/>). ItemID <see cref="PortFilterRules.Everything"/>
    /// marks the side that takes every item no other rule names.
    /// </summary>
    [InternalBufferCapacity(4)]
    public struct OutputPortFilter : IBufferElementData
    {
        public int Facing;
        public int ItemID;
    }

    /// <summary>Distinct items that have reached a Filter, newest last, so its panel can offer them as choices.</summary>
    [InternalBufferCapacity(8)]
    public struct FilterSeenItem : IBufferElementData
    {
        public int ItemID;
    }
}
