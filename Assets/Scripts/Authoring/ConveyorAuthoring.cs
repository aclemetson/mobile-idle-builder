using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Legacy authoring component — kept for SubScene compatibility.
    /// Runtime conveyor belts are created by ConveyorPlacer (not baked from SubScenes).
    /// </summary>
    public class ConveyorAuthoring : MonoBehaviour
    {
        public class Baker : Baker<ConveyorAuthoring>
        {
            public override void Bake(ConveyorAuthoring authoring)
            {
                // No-op: runtime belts are placed by ConveyorPlacer.
            }
        }
    }
}
