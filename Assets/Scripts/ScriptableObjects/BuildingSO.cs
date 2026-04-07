using UnityEngine;

namespace MobileIdleBuilder
{
    [CreateAssetMenu(fileName = "New Building", menuName = "MobileIdleBuilder/Building")]
    public class BuildingSO : ScriptableObject
    {
        public int buildingId;
        public string buildingName;
        public int tier;
        public float basePowerDraw;
        public float baseProductionSpeed = 1f;
    }
}
