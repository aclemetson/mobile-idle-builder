using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder
{
    public class PowerNodeAuthoring : MonoBehaviour
    {
        public float maxEV = 100f;
        public float influenceRadius = 5f;
        public float linkRadius = 10f;
        public bool isGridLinked = false;

        public class Baker : Baker<PowerNodeAuthoring>
        {
            public override void Bake(PowerNodeAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new PowerNodeData
                {
                    MaxEV           = authoring.maxEV,
                    CurrentEV       = authoring.maxEV,
                    InfluenceRadius = authoring.influenceRadius,
                    LinkRadius      = authoring.linkRadius,
                    IsGridLinked    = authoring.isGridLinked
                });
            }
        }
    }
}
