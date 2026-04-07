using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace MobileIdleBuilder
{
    public class ConveyorAuthoring : MonoBehaviour
    {
        public Vector2Int source;
        public Vector2Int destination;
        public float speed = 1f;

        public class Baker : Baker<ConveyorAuthoring>
        {
            public override void Bake(ConveyorAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new ConveyorData
                {
                    Source        = new int2(authoring.source.x, authoring.source.y),
                    Destination   = new int2(authoring.destination.x, authoring.destination.y),
                    Speed         = authoring.speed,
                    CarriedItemID = -1
                });
            }
        }
    }
}
