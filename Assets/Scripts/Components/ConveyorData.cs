using Unity.Entities;
using Unity.Mathematics;

namespace MobileIdleBuilder
{
    public struct ConveyorData : IComponentData
    {
        public int2 Source;
        public int2 Destination;
        public float Speed;
        public int CarriedItemID;
    }
}
