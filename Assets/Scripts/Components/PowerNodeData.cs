using Unity.Entities;

namespace MobileIdleBuilder
{
    public struct PowerNodeData : IComponentData
    {
        public float MaxEV;
        public float CurrentEV;
        public float InfluenceRadius;
        public float LinkRadius;
        public bool IsGridLinked;
    }
}
