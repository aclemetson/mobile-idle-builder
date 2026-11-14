using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Marks a Belt Splitter: every item that comes in leaves by one of its output sides, taking turns, so one
    /// belt forks into two or three. No filter: any item goes to any side.
    /// </summary>
    public struct SplitterTag : IComponentData { }

    /// <summary>Whose turn it is: the output side (<see cref="OutputDirection"/>) that gets the next item.</summary>
    public struct SplitterState : IComponentData
    {
        public int NextFacing;
    }
}
