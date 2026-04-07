using Unity.Entities;
using Unity.Mathematics;

namespace MobileIdleBuilder
{
    public struct GridPosition : IComponentData
    {
        public int2 Cell;
    }
}
