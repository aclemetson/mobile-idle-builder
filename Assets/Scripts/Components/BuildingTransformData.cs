using Unity.Entities;

namespace MobileIdleBuilder
{
    public struct BuildingTransformData : IComponentData
    {
        public int  Rotation; // 0-3 CW
        public bool Flipped;
    }
}
