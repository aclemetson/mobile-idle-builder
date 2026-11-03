using Unity.Entities;

namespace MobileIdleBuilder
{
    public struct BuildingData : IComponentData
    {
        public int BuildingType;
        public int UpgradeLevel;
        public int StorageUpgradeLevel;
        public int InputUpgradeLevel;
        public float ProductionSpeed;
        public bool IsActive;
    }
}
